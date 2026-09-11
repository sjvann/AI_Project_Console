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
    bool IsUi = false,
    string IconPath = "",
    string StackId = "");

public sealed record ScanResult(
    string Root,
    IReadOnlyList<ProjectInfo> Projects,
    string Error = "",
    IReadOnlyList<string>? StackIds = null);

public sealed record ProductLine(string Id, string Label, string Root);

/// <summary>
/// 另一個行程的啟動相依。與 <see cref="ServiceEntry.HostedBy"/>（同一行程別名）不同。
/// </summary>
public sealed record ServiceDependency(string Id, bool Optional = false);

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
    string Source = "scan",
    IReadOnlyList<ServiceDependency>? DependsOn = null,
    string? Ready = null,
    int? ReadyTimeoutMs = null,
    string IconPath = "")
{
    public IReadOnlyList<ServiceDependency> Dependencies =>
        DependsOn is { Count: > 0 } ? DependsOn : [];
}

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
    string Reason = "",
    string IconPath = "");

public sealed record ConsoleAction(
    string Id,
    string Label,
    string Group,
    string Handler,
    bool RequiresGithub = false,
    bool RequiresDeploy = false,
    string? Confirm = null,
    string Lane = "");

public sealed record BuildFailure(string Target, int ExitCode, string Log);

public sealed record GitBriefStatus(
    string Branch,
    int DirtyCount,
    int? Ahead,
    int? Behind,
    bool HasUpstream = true)
{
    public bool IsClearToLeave => LeaveBlockReason() is null;

    public static string LeaveGateTitle(string action) => $"還不能{action}";

    public static string LeaveGateForceLabel(string action) => action switch
    {
        "離開" or "關閉專案" => "強行關閉",
        _ => $"強行{action}",
    };

    public const string LeaveGateHint = "請先在 Pulse 提交，或開 GitHub 操作台發布。專案列的「分支」可確認目前分支。";

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
        var meta = PulseMeta();
        if (!string.IsNullOrEmpty(meta))
            parts.Add(meta);
        return string.Join(" · ", parts);
    }

    public string PulseMeta()
    {
        var parts = new List<string>();
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

public readonly record struct GitBriefProbe(bool IsRepo, GitBriefStatus? Brief);

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
