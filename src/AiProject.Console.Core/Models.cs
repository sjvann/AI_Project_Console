using System.Text.Json.Nodes;

namespace AiProject.Console.Core;

public sealed record ProjectInfo(
    string RelDir,
    string Name,
    string Csproj,
    string Sdk,
    string OutputType,
    bool IsExecutable,
    bool IsWeb,
    bool IsWebApi,
    bool IsTest,
    IReadOnlyList<int> Ports,
    IReadOnlyList<string> ApplicationUrls,
    string LaunchUrl,
    string Group,
    string Language = "",
    bool IsUi = false);

public sealed record ScanResult(string Root, IReadOnlyList<ProjectInfo> Projects, string Error = "");

public sealed record ProductLine(string Id, string Label, string Root);

public sealed record ServiceEntry(
    string Id,
    string Label,
    string Stem,
    string Project,
    int? Port,
    string Health,
    string OpenUrl,
    string Group,
    string? HostedBy = null,
    string? AspnetUrls = null,
    string? PreStart = null,
    string Source = "scan");

public sealed class ProjectCatalog
{
    public required string Root { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<ServiceEntry> Services { get; init; }
    public required IReadOnlyList<ProjectInfo> Projects { get; init; }
    public required IReadOnlyList<string> StartOrder { get; init; }
    public required string Frontend { get; init; }
    public required JsonObject Manifest { get; init; }
    public required ScanResult Scan { get; init; }
    public string Summary { get; init; } = "";
}

public sealed record BuildState(
    string Id,
    string Name,
    string Status,
    string Path,
    string System = "",
    string Kind = "",
    string Output = "",
    string Label = "",
    string Language = "",
    string Activity = "",
    DateTimeOffset? LastBuildUtc = null,
    DateTimeOffset? NewestSourceUtc = null,
    string NewestSourcePath = "",
    string Reason = "");

public sealed record ConsoleAction(
    string Id,
    string Label,
    string Group,
    string Handler,
    bool RequiresGithub = false,
    bool RequiresDeploy = false,
    string? Confirm = null);

public sealed record BuildFailure(string Target, int ExitCode, string Log);

public sealed record GitBriefStatus(
    string Branch,
    int DirtyCount,
    int? Ahead,
    int? Behind,
    bool HasUpstream = true)
{
    public bool IsClearToLeave => LeaveBlockReason() is null;

    public string? LeaveBlockReason()
    {
        if (DirtyCount > 0)
            return $"工作區有 {DirtyCount} 筆未提交變更。請先提交或還原，工作區乾淨後才能離開。";
        if (!HasUpstream)
            return $"目前分支「{Branch}」尚未設定遠端追蹤。請先發布（push）此分支，本機與遠端一致後才能離開。";
        if (Ahead is > 0)
            return $"目前分支「{Branch}」有 {Ahead} 個尚未發布的提交。請先發布（push），本機與遠端一致後才能離開。";
        return null;
    }

    public string Format()
    {
        var parts = new List<string> { Branch };
        if (DirtyCount > 0)
            parts.Add($"{DirtyCount} 未提交");
        if (Ahead is > 0)
            parts.Add($"↑{Ahead}");
        if (Behind is > 0)
            parts.Add($"↓{Behind}");
        if (!HasUpstream)
            parts.Add("無遠端追蹤");
        else if (DirtyCount == 0 && Ahead is not > 0 && Behind is not > 0)
            parts.Add("乾淨");
        return string.Join(" · ", parts);
    }
}

public sealed record GitBranchInfo(
    string Name,
    bool IsCurrent,
    bool IsRemote,
    string? Tracking,
    string ShortSha)
{
    public string LocalName => IsRemote ? StripRemotePrefix(Name) : Name;

    public string Meta()
    {
        var parts = new List<string>();
        if (IsRemote)
            parts.Add("遠端");
        if (!string.IsNullOrEmpty(Tracking))
            parts.Add(Tracking);
        if (!string.IsNullOrEmpty(ShortSha))
            parts.Add(ShortSha);
        return string.Join(" · ", parts);
    }

    public static string StripRemotePrefix(string name)
    {
        var text = (name ?? "").Trim();
        var slash = text.IndexOf('/');
        return slash > 0 ? text[(slash + 1)..] : text;
    }
}
