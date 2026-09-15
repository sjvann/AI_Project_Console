using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed record ContributorRollup(
    Guid PersonId,
    string DisplayName,
    string? GitHubKey,
    decimal Hours,
    int SlotCount,
    IReadOnlyList<int> IssueNumbers,
    IReadOnlyList<TypeCount> Types);

public sealed record TypeCount(string Type, int Count);

public sealed record ProjectContributionView(
    Guid ProjectId,
    string ProjectName,
    decimal TotalHours,
    int ContributorCount,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<ContributorRollup> Contributors,
    IReadOnlyList<TypeCount> TypeTotals,
    string? EmptyHint);

public sealed class ContributionQueries
{
    readonly ITimesheetRepository _timesheets;
    readonly IPersonRepository _people;
    readonly IProjectRepository _projects;
    readonly IAuthorizationGate _auth;
    readonly ICurrentUser _user;

    public ContributionQueries(
        ITimesheetRepository timesheets,
        IPersonRepository people,
        IProjectRepository projects,
        IAuthorizationGate auth,
        ICurrentUser user)
    {
        _timesheets = timesheets;
        _people = people;
        _projects = projects;
        _auth = auth;
        _user = user;
    }

    public async Task<ProjectContributionView?> ForProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return null;

        var sheets = (await _timesheets.ListForProjectAsync(projectId, ct)).ToList();
        if (!CanSeeAll(projectId))
        {
            if (_user.PersonId is Guid pid)
                sheets = sheets.Where(t => t.PersonId == pid).ToList();
            else
                sheets = [];
        }

        // 非退回加總；更正為新列（已核准不可覆蓋），一併計入。
        var effective = sheets.Where(t => t.Status != TimesheetStatus.Returned).ToList();
        if (effective.Count == 0)
        {
            return new ProjectContributionView(
                project.Id,
                project.Name,
                0,
                0,
                null,
                null,
                [],
                [],
                "還沒有上傳到這個專案的工時。工程師用公開回報契約送出後會出現在這裡。");
        }

        var people = (await _people.ListAsync(ct)).ToDictionary(p => p.Id);
        var contributors = effective
            .GroupBy(t => t.PersonId)
            .Select(g =>
            {
                people.TryGetValue(g.Key, out var person);
                var issues = g.SelectMany(t => t.IssueNumbers).Where(n => n > 0).Distinct().OrderBy(n => n).ToList();
                var types = g.SelectMany(t => t.ContributionTypes)
                    .GroupBy(t => t, StringComparer.Ordinal)
                    .Select(x => new TypeCount(x.Key, x.Count()))
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Type, StringComparer.Ordinal)
                    .ToList();
                return new ContributorRollup(
                    g.Key,
                    person?.DisplayName ?? g.Key.ToString()[..8],
                    person?.GitHubKey,
                    g.Sum(t => t.Hours),
                    g.Count(),
                    issues,
                    types);
            })
            .OrderByDescending(c => c.Hours)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var typeTotals = effective
            .SelectMany(t => t.ContributionTypes)
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(x => new TypeCount(x.Key, x.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Type, StringComparer.Ordinal)
            .ToList();

        return new ProjectContributionView(
            project.Id,
            project.Name,
            effective.Sum(t => t.Hours),
            contributors.Count,
            effective.Min(t => t.WorkDate),
            effective.Max(t => t.WorkDate),
            contributors,
            typeTotals,
            null);
    }

    bool CanSeeAll(Guid projectId) =>
        _auth.Can(PlatformCapability.ConfirmTimesheet, projectId: projectId)
        || _auth.Can(PlatformCapability.ManageProjects, projectId: projectId)
        || _auth.Can(PlatformCapability.LockPayroll);
}
