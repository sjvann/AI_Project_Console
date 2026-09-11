namespace AiProject.Console.Core.Tech;

public sealed record ToolSpec(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Commands,
    string WingetId,
    string DownloadUrl,
    string HowTo);

public sealed record TechStackProfile(
    string Id,
    string Language,
    IReadOnlyList<string> ExactFileNames,
    IReadOnlyList<string> FileSuffixes,
    IReadOnlyList<string> SourceExtensions,
    IReadOnlyList<string> RequiredToolIds,
    int ManifestRank = 50,
    IReadOnlyList<string>? SkipIfSameDirHas = null,
    bool KeepAllInDirectory = false)
{
    public IReadOnlyList<string> SkipIfSameDirHasNames => SkipIfSameDirHas ?? [];
}

public sealed record ProcessPlan(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyList<string> MissingToolIds,
    string Display,
    IReadOnlyDictionary<string, string>? ExtraEnv = null);

public sealed record ToolStatus(ToolSpec Spec, bool Installed, string? ResolvedPath);
