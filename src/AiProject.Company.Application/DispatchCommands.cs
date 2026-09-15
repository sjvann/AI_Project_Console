using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class DispatchCommands
{
    readonly IPersonRepository _people;
    readonly IProjectRepository _projects;
    readonly IContractRepository _contracts;
    readonly IClientRepository _clients;
    readonly IAssignmentRepository _assignments;
    readonly ISettingsRepository _settings;
    readonly IGitHubDirectory _github;
    readonly IAuthorizationGate _auth;
    readonly IAvailabilityCalculator _availability;
    readonly IContractStaffingPolicy _staffing;
    readonly IAssignmentSuggester _suggester;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public DispatchCommands(
        IPersonRepository people,
        IProjectRepository projects,
        IContractRepository contracts,
        IClientRepository clients,
        IAssignmentRepository assignments,
        ISettingsRepository settings,
        IGitHubDirectory github,
        IAuthorizationGate auth,
        IAvailabilityCalculator availability,
        IContractStaffingPolicy staffing,
        IAssignmentSuggester suggester,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock,
        ICurrentUser user)
    {
        _people = people;
        _projects = projects;
        _contracts = contracts;
        _clients = clients;
        _assignments = assignments;
        _settings = settings;
        _github = github;
        _auth = auth;
        _availability = availability;
        _staffing = staffing;
        _suggester = suggester;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome<Guid>> AssignAsync(
        Guid personId,
        Guid projectId,
        DateOnly start,
        DateOnly end,
        decimal hoursPerWeek,
        AssignmentRole role,
        IEnumerable<(string Repo, int Number)> issues,
        Guid? milestoneId,
        string? forceReason,
        CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.Dispatch, projectId: projectId);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var person = await _people.GetAsync(personId, ct);
        var project = await _projects.GetAsync(projectId, ct);
        if (person is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("人員"));
        if (project is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var contract = await _contracts.GetAsync(project.ContractId, ct);
        if (contract is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("合約"));
        var client = await _clients.GetAsync(contract.ClientId, ct);
        if (client is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var clientGate = client.EnsureCanDispatch();
        if (!clientGate.Ok)
            return Outcome<Guid>.Fail(clientGate.Code, clientGate.Message);
        var weekStart = start.AddDays(-((int)start.DayOfWeek + 6) % 7);
        var weekAssignments = await _assignments.ListForWeekAsync(weekStart, ct);
        var availability = _availability.ForWeek(person, weekStart, weekAssignments);
        var settings = await _settings.GetAsync(ct);
        var canForce = _auth.Can(PlatformCapability.ForceOverload);
        var issueList = issues.Where(i => i.Number > 0).ToList();
        var source = issueList.Count > 0 ? AssignmentSource.FromIssue : AssignmentSource.Manual;
        var created = Assignment.Create(person, project, contract, start, end, hoursPerWeek, role, source, _staffing, availability, forceReason, canForce, settings.WriteBackGithubAssignee, _clock.UtcNow);
        if (!created.Ok)
            return Outcome<Guid>.Fail(created.Code, created.Message);
        var assignment = created.Value!;
        assignment.AttachIssues(issueList);
        assignment.AttachMilestone(milestoneId);
        await _assignments.AddAsync(assignment, ct);
        await _uow.SaveChangesAsync(ct);
        if (!string.IsNullOrWhiteSpace(forceReason) && availability.AssignedHours + hoursPerWeek > availability.CapHours)
            await _audit.SensitiveChange(AuditActions.ForceOverload, "Assignment", assignment.Id, forceReason, availability, hoursPerWeek, ct);
        if (settings.WriteBackGithubAssignee)
            await TryWriteBackAsync(assignment, project, person.GitHubLogin, ct);
        return Outcome<Guid>.Success(assignment.Id);
    }

    public async Task<Outcome> CancelAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var assignment = await _assignments.GetAsync(assignmentId, ct);
        if (assignment is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("派工單"));
        var gate = _auth.Ensure(PlatformCapability.Dispatch, projectId: assignment.ProjectId);
        if (!gate.Ok)
            return gate;
        assignment.Cancel();
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> RetrySyncAsync(Guid assignmentId, CancellationToken ct = default)
    {
        var assignment = await _assignments.GetAsync(assignmentId, ct);
        if (assignment is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("派工單"));
        var gate = _auth.Ensure(PlatformCapability.Dispatch, projectId: assignment.ProjectId);
        if (!gate.Ok)
            return gate;
        var project = await _projects.GetAsync(assignment.ProjectId, ct);
        var person = await _people.GetAsync(assignment.PersonId, ct);
        if (project is null || person is null)
            return Outcome.Fail(ErrorCodes.InvalidState, "找不到專案或人員。");
        await TryWriteBackAsync(assignment, project, person.GitHubLogin, ct);
        return assignment.SyncState == AssignmentSyncState.Synced
            ? Outcome.Success()
            : Outcome.Fail(ErrorCodes.InvalidState, assignment.SyncNote ?? "寫回仍待同步。");
    }

    public async Task<IReadOnlyList<AssignmentSuggestion>> SuggestAsync(Guid projectId, DateOnly weekStart, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct) ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var people = await _people.ListAsync(ct);
        var week = await _assignments.ListForWeekAsync(weekStart, ct);
        return _suggester.Suggest(project, people, week, weekStart);
    }

    async Task TryWriteBackAsync(Assignment assignment, Project project, string? login, CancellationToken ct)
    {
        if (assignment.IssueNumbers.Count == 0)
        {
            assignment.MarkSyncNotRequired("未關聯 Issue，無需寫回 assignee。");
            await _uow.SaveChangesAsync(ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(login))
        {
            assignment.MarkPendingSync("人員未綁定 GitHub，無法寫回 assignee。");
            await _uow.SaveChangesAsync(ct);
            return;
        }
        if (project.Repos.Count == 0)
        {
            assignment.MarkPendingSync("專案尚未掛倉，無法寫回 assignee。");
            await _uow.SaveChangesAsync(ct);
            return;
        }

        var repoByNumber = await ResolveIssueReposAsync(assignment, project, ct);
        var failures = new List<string>();
        for (var i = 0; i < assignment.IssueNumbers.Count; i++)
        {
            var number = assignment.IssueNumbers[i];
            if (!repoByNumber.TryGetValue(number, out var repo) || string.IsNullOrWhiteSpace(repo))
            {
                failures.Add($"#{number} 找不到所屬倉");
                continue;
            }
            var result = await _github.AddAssigneeAsync(repo, number, login, ct);
            if (!result.Ok)
                failures.Add($"#{number}@{repo}：{result.Message}");
        }

        if (failures.Count == 0)
            assignment.MarkSynced();
        else
            assignment.MarkPendingSync(string.Join("；", failures.Take(3)));
        await _uow.SaveChangesAsync(ct);
    }

    async Task<Dictionary<int, string>> ResolveIssueReposAsync(Assignment assignment, Project project, CancellationToken ct)
    {
        var map = new Dictionary<int, string>();
        for (var i = 0; i < assignment.IssueNumbers.Count; i++)
        {
            var number = assignment.IssueNumbers[i];
            var stored = i < assignment.IssueRepos.Count ? assignment.IssueRepos[i] : "";
            if (!string.IsNullOrWhiteSpace(stored))
                map[number] = stored;
        }

        var missing = assignment.IssueNumbers.Where(n => !map.ContainsKey(n)).ToList();
        if (missing.Count == 0)
            return map;

        foreach (var repo in project.Repos)
        {
            var open = await _github.ListOpenIssuesAsync(repo.OwnerRepo, ct);
            foreach (var issue in open.Where(i => missing.Contains(i.Number)))
                map[issue.Number] = string.IsNullOrWhiteSpace(issue.Repo) ? repo.OwnerRepo : issue.Repo;
        }

        foreach (var number in missing.Where(n => !map.ContainsKey(n)))
            map[number] = project.Repos[0].OwnerRepo;
        return map;
    }
}
