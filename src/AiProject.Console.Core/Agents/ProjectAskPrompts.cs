namespace AiProject.Console.Core.Agents;

public sealed record ProjectAskSuggestion(string Id, string Title, string Prompt, string? HighlightWhen);

public sealed record ProjectAskSuggestionView(string Id, string Title, string Prompt, bool Highlighted);

/// <summary>
/// 專案問答的固定題庫；依值班數字把相關題目置頂並標「建議」。
/// </summary>
public static class ProjectAskPrompts
{
    public const string WhenOffline = "offline";
    public const string WhenStale = "stale";
    public const string WhenAudit = "audit";

    public static readonly IReadOnlyList<ProjectAskSuggestion> Catalog =
    [
        new("handoff", "現在能不能交班？", "現在能不能交班？請先看值班摘要。", null),
        new("offline", "哪些服務離線？", "哪些服務離線？為什麼？", WhenOffline),
        new("stale", "哪些專案需重編？", "哪些專案需重編？原因是什麼？", WhenStale),
        new("audit", "最近 MCP 為什麼被拒絕？", "最近 MCP 為什麼被拒絕？", WhenAudit),
        new("doctor", "環境體檢有沒有缺工具？", "環境體檢有沒有缺工具？", null),
        new("git", "git 現在乾不乾淨？", "git 現在乾不乾淨？領先或落後遠端嗎？", null),
    ];

    public static IReadOnlyList<ProjectAskSuggestionView> Rank(int offline, int staleProjects, int auditFails)
    {
        bool Hit(string? when) => when switch
        {
            WhenOffline => offline > 0,
            WhenStale => staleProjects > 0,
            WhenAudit => auditFails > 0,
            _ => false,
        };

        var hits = new List<ProjectAskSuggestionView>();
        var rest = new List<ProjectAskSuggestionView>();
        foreach (var item in Catalog)
        {
            var view = new ProjectAskSuggestionView(item.Id, item.Title, item.Prompt, Hit(item.HighlightWhen));
            if (view.Highlighted)
                hits.Add(view);
            else
                rest.Add(view);
        }

        return hits.Concat(rest).ToList();
    }
}
