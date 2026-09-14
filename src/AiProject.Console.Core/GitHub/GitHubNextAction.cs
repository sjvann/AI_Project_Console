namespace AiProject.Console.Core.GitHub;

public static class GithubActionLanes
{
    public const string Daily = "daily";
    public const string Collab = "collab";
    public const string Ship = "ship";
    public const string Setup = "setup";

    public static readonly (string Id, string Title, string Hint)[] All =
    [
        (Daily, "日常節奏", "分支、提交、同步、發布"),
        (Collab, "遠端協作", "倉庫、PR、任務"),
        (Ship, "發行與 CI", "Release、Actions"),
        (Setup, "接入", "clone、遠端設定"),
    ];
}

public sealed record GitHubNextStep(
    string Kind,
    string Handler,
    string Label,
    string Hint,
    string Lane,
    bool OpensHub = false);

public static class GitHubNextAction
{
    public const string KindCommit = "commit";
    public const string KindSync = "sync";
    public const string KindPublish = "publish";
    public const string KindPr = "pr";
    public const string KindActions = "actions";
    public const string KindRelease = "release";
    public const string KindHub = "hub";

    public static string? EmptyPulseMeta(bool? isGitRepo) =>
        isGitRepo == false ? "不是 git 倉" : null;

    public static string EmptyPulseHint(bool? isGitRepo) =>
        isGitRepo == false
            ? "這個目錄不是 git 倉庫。可從操作台接入遠端或看全部動作。"
            : "尚未讀到 git 狀態。可從操作台接入遠端或看全部動作。";

    public static GitHubNextStep Decide(
        GitBriefStatus? brief,
        bool hasPr,
        string defaultBranch = "main",
        bool ciInProgress = false,
        bool ciJustFailed = false,
        bool? isGitRepo = null)
    {
        if (brief is { DirtyCount: > 0 } dirty)
        {
            return new GitHubNextStep(
                KindCommit,
                "github_commit",
                "提交…",
                $"工作區有 {dirty.DirtyCount} 筆未提交變更。離開前請先提交。",
                GithubActionLanes.Daily);
        }

        if (brief is { Behind: > 0 } behind)
        {
            return new GitHubNextStep(
                KindSync,
                "github_sync",
                "同步（pull）",
                $"目前分支落後遠端 {behind.Behind} 個提交。請先同步。",
                GithubActionLanes.Daily);
        }

        if (brief is { HasUpstream: false } noTrack)
        {
            return new GitHubNextStep(
                KindPublish,
                "github_publish",
                "發布（push）",
                $"目前分支「{noTrack.Branch}」尚未設定遠端追蹤。請先發布。",
                GithubActionLanes.Daily);
        }

        if (brief is { Ahead: > 0 } ahead)
        {
            return new GitHubNextStep(
                KindPublish,
                "github_publish",
                "發布（push）",
                $"目前分支有 {ahead.Ahead} 個尚未發布的提交。請先發布。",
                GithubActionLanes.Daily);
        }

        if (brief is not null && !IsDefaultBranch(brief.Branch, defaultBranch) && !hasPr)
        {
            return new GitHubNextStep(
                KindPr,
                "github_pr",
                "建立／開啟 PR",
                $"分支「{brief.Branch}」還沒有 PR。建立後才能請人審。",
                GithubActionLanes.Collab);
        }

        if (ciInProgress)
        {
            return new GitHubNextStep(
                KindActions,
                "github_actions",
                "Actions 狀態…",
                "遠端 CI 進行中。控制台不能修 CI；點一下看最近執行。",
                GithubActionLanes.Ship);
        }

        if (ciJustFailed)
        {
            return new GitHubNextStep(
                KindActions,
                "github_actions",
                "Actions 狀態…",
                "剛推送的 CI 失敗。點一下到 GitHub 看 log。",
                GithubActionLanes.Ship);
        }

        if (brief is not null && IsDefaultBranch(brief.Branch, defaultBranch))
        {
            return new GitHubNextStep(
                KindRelease,
                "github_release",
                "發行 Release…",
                "已與遠端一致。可發行正式版。",
                GithubActionLanes.Ship);
        }

        return new GitHubNextStep(
            KindHub,
            "",
            "全部動作",
            brief is null ? EmptyPulseHint(isGitRepo) : "已與遠端一致。其餘動作在操作台。",
            GithubActionLanes.Daily,
            OpensHub: true);
    }

    public static bool IsDefaultBranch(string? branch, string? defaultBranch)
    {
        if (string.IsNullOrWhiteSpace(branch) || string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase))
            return true;
        var expected = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim();
        return string.Equals(branch, expected, StringComparison.OrdinalIgnoreCase);
    }
}
