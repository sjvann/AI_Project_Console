namespace AiProject.Console.Core.Catalog;

public sealed record CatalogRefreshDiff(
    IReadOnlyList<string> AddedProjects,
    IReadOnlyList<string> RemovedProjects,
    IReadOnlyList<string> AddedServices,
    IReadOnlyList<string> RemovedServices)
{
    public bool HasChanges =>
        AddedProjects.Count > 0
        || RemovedProjects.Count > 0
        || AddedServices.Count > 0
        || RemovedServices.Count > 0;

    public string Format()
    {
        if (!HasChanges)
            return "已重新掃描，專案與服務沒有增減";
        var parts = new List<string>();
        Append(parts, "專案", AddedProjects, "新增");
        Append(parts, "專案", RemovedProjects, "移除");
        Append(parts, "服務", AddedServices, "新增");
        Append(parts, "服務", RemovedServices, "移除");
        return "已重新掃描：" + string.Join("、", parts);
    }

    private static void Append(List<string> parts, string kind, IReadOnlyList<string> names, string verb)
    {
        if (names.Count == 0)
            return;
        var shown = names.Count <= 3
            ? string.Join("、", names)
            : string.Join("、", names.Take(3)) + "…";
        parts.Add($"{verb} {names.Count} 個{kind}（{shown}）");
    }
}

/// <summary>
/// 比較兩次工作區掃描結果：開啟中的方案新增或移除專案時，控制台要靠重新掃描才看得到。
/// </summary>
public static class CatalogRefresh
{
    public static CatalogRefreshDiff Diff(ProjectCatalog previous, ProjectCatalog next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        var oldPrj = previous.Projects.Select(ProjectKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPrj = next.Projects.Select(ProjectKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldSvc = previous.Services.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newSvc = next.Services.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CatalogRefreshDiff(
            AddedProjects: next.Projects.Where(p => !oldPrj.Contains(ProjectKey(p))).Select(DisplayProject).ToList(),
            RemovedProjects: previous.Projects.Where(p => !newPrj.Contains(ProjectKey(p))).Select(DisplayProject).ToList(),
            AddedServices: next.Services.Where(s => !oldSvc.Contains(s.Id)).Select(s => s.Label).ToList(),
            RemovedServices: previous.Services.Where(s => !newSvc.Contains(s.Id)).Select(s => s.Label).ToList());
    }

    internal static string ProjectKey(ProjectInfo info)
    {
        var rel = (info.RelDir ?? "").Replace('\\', '/').Trim();
        return string.IsNullOrEmpty(rel) ? info.Name : rel;
    }

    private static string DisplayProject(ProjectInfo info) =>
        string.IsNullOrWhiteSpace(info.Name) ? ProjectKey(info) : info.Name;
}
