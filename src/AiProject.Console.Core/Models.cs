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
    string Group);

public sealed record ScanResult(string Root, IReadOnlyList<ProjectInfo> Projects, string Error = "");

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
    string Label = "");

public sealed record ConsoleAction(
    string Id,
    string Label,
    string Group,
    string Handler,
    bool RequiresGithub = false,
    bool RequiresDeploy = false,
    string? Confirm = null);

public sealed record BuildFailure(string Target, int ExitCode, string Log);

public sealed record GitBriefStatus(string Branch, int DirtyCount, int? Ahead, int? Behind)
{
    public string Format()
    {
        var parts = new List<string> { Branch };
        if (DirtyCount > 0)
            parts.Add($"{DirtyCount} 未提交");
        if (Ahead is > 0)
            parts.Add($"↑{Ahead}");
        if (Behind is > 0)
            parts.Add($"↓{Behind}");
        if (DirtyCount == 0 && Ahead is not > 0 && Behind is not > 0)
            parts.Add("乾淨");
        return string.Join(" · ", parts);
    }
}
