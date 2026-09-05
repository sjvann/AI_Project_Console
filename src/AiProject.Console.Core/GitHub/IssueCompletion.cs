using System.Text.RegularExpressions;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 完成 Issue 的兩步：留言回報 ≠ 建立 PR。後者要在功能分支上，合併才關閉任務。
/// </summary>
public static class IssueCompletion
{
    public const string DialogHint =
        "先看這則任務的內容，再請 Agent 協助。做完之後才留言回報或開 PR。";

    public static string? CreatePrBlockReason(
        GitBriefStatus? brief,
        string? defaultBranch,
        bool hasPr,
        string? prReadError = null)
    {
        if (hasPr)
            return null;
        if (!string.IsNullOrWhiteSpace(prReadError)
            && !PrStatus.LooksLikeNoPr(prReadError))
            return ExplainCreatePrFailure(prReadError);
        if (brief is null)
            return "尚未讀到 git 狀態，無法建立 PR。";
        if (GitHubNextAction.IsDefaultBranch(brief.Branch, defaultBranch))
            return $"目前在預設分支「{brief.Branch}」，不能對自己開 PR。請先為此任務建立功能分支，再提交、發布，然後建立 PR。";
        if (brief.DirtyCount > 0)
            return $"工作區有 {brief.DirtyCount} 筆未提交變更。「送出回應」只會寫進 Issue 討論，不會把程式改動送上 GitHub。請先提交，再發布並建立 PR。";
        if (brief.Behind is > 0)
            return $"目前分支落後遠端 {brief.Behind} 個提交。請先同步，再建立 PR。";
        if (!brief.HasUpstream)
            return $"目前分支「{brief.Branch}」尚未發布到遠端。請先發布，再建立 PR。";
        if (brief.Ahead is > 0)
            return $"目前分支有 {brief.Ahead} 個尚未發布的提交。請先發布，再建立 PR。";
        return null;
    }

    public static bool NeedsTaskBranch(GitBriefStatus? brief, string? defaultBranch) =>
        brief is not null && GitHubNextAction.IsDefaultBranch(brief.Branch, defaultBranch);

    public static string SuggestIssueBranchName(int number, string? title)
    {
        var slug = Slug(title);
        return string.IsNullOrEmpty(slug) ? "issue-" + number : "issue-" + number + "-" + slug;
    }

    public static string ExplainCreatePrFailure(string? output)
    {
        var text = output ?? "";
        if (LooksLikeNoCommits(text))
            return "目前分支相對預設分支沒有新提交，無法建立 PR。請先在功能分支上提交變更並發布。";
        if (ContainsAny(text, "already exists", "already has a pull request"))
            return "這個分支已經有 PR。請改按「開啟 PR」。";
        if (ContainsAny(text, "must be on a branch", "cannot create a pull request for", "Head ref must be a branch"))
            return "不能從預設分支開 PR。請先為此任務建立功能分支。";
        if (ContainsAny(text, "needs to be pushed", "not fully pushed", "has no upstream"))
            return "目前分支尚未完整發布到遠端。請先發布，再建立 PR。";
        if (ContainsAny(text, "GitHub CLI", "gh: command not found", "executable file not found"))
            return "需要 GitHub CLI（gh）才能建立 PR。請安裝：https://cli.github.com/";
        var line = FirstLine(text);
        return string.IsNullOrEmpty(line) ? "建立 PR 失敗。" : line;
    }

    public static bool LooksLikeNoCommits(string? output)
    {
        var text = output ?? "";
        return ContainsAny(
            text,
            "No commits between",
            "could not find any commits",
            "could not compute title",
            "could not compute title or body");
    }

    static string Slug(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";
        var chars = title.Select(c =>
                char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c)
                : c is ' ' or '-' or '_' or '/' ? '-'
                : '\0')
            .Where(c => c != '\0')
            .ToArray();
        var slug = Regex.Replace(new string(chars), "-+", "-").Trim('-');
        if (slug.Length > 40)
            slug = slug[..40].TrimEnd('-');
        return string.Equals(slug, "issue", StringComparison.OrdinalIgnoreCase) ? "" : slug;
    }

    static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    static string FirstLine(string text)
    {
        var t = text.Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }
}
