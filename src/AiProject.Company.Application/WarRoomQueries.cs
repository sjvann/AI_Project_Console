using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class WarRoomQueries
{
    readonly IProjectRepository _projects;
    readonly IAssignmentRepository _assignments;
    readonly ITimesheetRepository _timesheets;
    readonly IPersonRepository _people;
    readonly IClientRepository _clients;
    readonly IContractRepository _contracts;
    readonly IGitHubDirectory _github;
    readonly IAuthorizationGate _auth;
    readonly IProjectHealthPolicy _health;
    readonly IAvailabilityCalculator _availability;
    readonly IClock _clock;
    readonly ISettingsRepository _settings;
    readonly BudgetCommands _budget;
    readonly IProjectDocsCatalog _docs;

    public WarRoomQueries(
        IProjectRepository projects,
        IAssignmentRepository assignments,
        ITimesheetRepository timesheets,
        IPersonRepository people,
        IClientRepository clients,
        IContractRepository contracts,
        IGitHubDirectory github,
        IAuthorizationGate auth,
        IProjectHealthPolicy health,
        IAvailabilityCalculator availability,
        IClock clock,
        ISettingsRepository settings,
        BudgetCommands budget,
        IProjectDocsCatalog docs)
    {
        _projects = projects;
        _assignments = assignments;
        _timesheets = timesheets;
        _people = people;
        _clients = clients;
        _contracts = contracts;
        _github = github;
        _auth = auth;
        _health = health;
        _availability = availability;
        _clock = clock;
        _settings = settings;
        _budget = budget;
        _docs = docs;
    }

    public async Task<WarRoomSnapshot> LoadAsync(CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ViewWarRoom);
        if (!gate.Ok)
            throw new DomainException(gate.Code, gate.Message);
        var settings = await _settings.GetAsync(ct);
        var today = _clock.Today(settings.TimeZoneId);
        var weekStart = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var clients = (await _clients.ListAsync(ct)).ToDictionary(c => c.Id);
        var contracts = (await _contracts.ListAsync(ct)).ToDictionary(c => c.Id);
        var projects = (await _projects.ListAsync(ct)).Where(p =>
        {
            if (p.IsDeleted || p.Status is ProjectStatus.Closed or ProjectStatus.Draft)
                return false;
            if (!contracts.TryGetValue(p.ContractId, out var contract))
                return false;
            return clients.TryGetValue(contract.ClientId, out var client) && client.CountsAsActiveDelivery;
        }).ToList();
        var assignments = await _assignments.ListAsync(ct);
        var people = await _people.ListAsync(ct);
        var sheets = await _timesheets.ListForPeriodAsync(weekStart, weekStart.AddDays(6), ct);
        var cards = new List<WarRoomCard>();
        var exceptions = new List<WarRoomException>();
        foreach (var project in projects)
        {
            decimal? margin = null;
            if (!project.ExcludeFromMarginKpi)
            {
                try
                {
                    var pnl = await _budget.PnlAsync(project.Id, ct: ct);
                    margin = pnl.Margin.ActualRate;
                }
                catch (DomainException)
                {
                    margin = null;
                }
            }
            var unassignedDays = 0;
            foreach (var repo in project.Repos)
            {
                var issues = await _github.ListOpenIssuesAsync(repo.OwnerRepo, ct);
                foreach (var issue in issues.Where(i => string.IsNullOrEmpty(i.AssigneeLogin)))
                {
                    var days = (int)(_clock.UtcNow - issue.UpdatedAt).TotalDays;
                    if (days > unassignedDays)
                        unassignedDays = days;
                    if (days >= 3)
                        exceptions.Add(new WarRoomException("unassigned", project.Id, $"{project.Name} 有未派 Issue #{issue.Number}", "/dispatch?project=" + project.Id));
                }
            }
            var assignedPeople = assignments.Where(a => a.ProjectId == project.Id && a.Status != AssignmentStatus.Cancelled && a.Overlaps(weekStart, weekStart.AddDays(6))).Select(a => a.PersonId).Distinct().ToList();
            var hoursMissing = assignedPeople.Any(pid => !sheets.Any(s => s.PersonId == pid));
            if (hoursMissing)
                exceptions.Add(new WarRoomException("hours", project.Id, project.Name + " 有已派人員尚未上傳工時", "/projects/" + project.Id));
            var docsReady = true;
            try
            {
                var catalog = await _docs.ReadAsync(project, ct);
                docsReady = catalog.RequirementsReady;
            }
            catch
            {
                docsReady = true;
            }
            var health = _health.Evaluate(project, today, unassignedDays, hoursMissing, margin, docsReady);
            if (health.Schedule == HealthTone.Red)
                exceptions.Add(new WarRoomException("overdue", project.Id, project.Name + " 逾期", "/projects/" + project.Id));
            if (health.Margin == HealthTone.Red)
                exceptions.Add(new WarRoomException("margin", project.Id, project.Name + " 毛利破門檻", "/projects/" + project.Id + "#pnl"));
            cards.Add(new WarRoomCard(project.Id, project.Name, project.CurrentPhase(_clock.UtcNow)?.Name ?? "", project.TargetEnd, assignedPeople.Count, health, margin));
        }
        foreach (var person in people.Where(p => p.Status == PersonStatus.Active && !p.IsDeleted))
        {
            var avail = _availability.ForWeek(person, weekStart, assignments);
            if (avail.Overload)
                exceptions.Add(new WarRoomException("overload", null, person.DisplayName + " 本週超載", "/dispatch"));
        }
        var idle = people.Count(p => p.Status == PersonStatus.Active && !p.IsDeleted && _availability.ForWeek(p, weekStart, assignments).AssignedHours == 0);
        var red = cards.Count(c => c.Health.Worst == HealthTone.Red);
        var marginCards = cards.Where(c => !projects.First(p => p.Id == c.ProjectId).ExcludeFromMarginKpi && c.MarginPercent is not null).ToList();
        var monthMargin = marginCards.Count == 0 ? (decimal?)null : marginCards.Average(c => c.MarginPercent!.Value);
        return new WarRoomSnapshot(projects.Count, red, idle, monthMargin, cards, exceptions, today);
    }
}

public sealed record WarRoomCard(Guid ProjectId, string Name, string Phase, DateOnly TargetEnd, int Headcount, ProjectHealth Health, decimal? MarginPercent);

public sealed record WarRoomException(string Kind, Guid? ProjectId, string Text, string Href);

public sealed record WarRoomSnapshot(int ActiveProjects, int RedCount, int IdlePeople, decimal? MonthMargin, IReadOnlyList<WarRoomCard> Cards, IReadOnlyList<WarRoomException> Exceptions, DateOnly Today);
