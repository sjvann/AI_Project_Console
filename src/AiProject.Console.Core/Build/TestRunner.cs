using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Tech;

namespace AiProject.Console.Core.Build;

/// <summary>
/// 本機一次跑完整測試（依偵測到的技術棧）。不是 IDE 的測試總管。
/// </summary>
public static class TestRunner
{
    public static string? FindSolution(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;
        root = Path.GetFullPath(root);
        return PickOne(Directory.GetFiles(root, "*.slnx"), root)
            ?? PickOne(Directory.GetFiles(root, "*.sln"), root);
    }

    public static IReadOnlyList<string> TargetsFor(ProjectCatalog catalog)
    {
        var targets = new List<string>();
        var hasDotnet = catalog.Projects.Any(p =>
            p.StackId is "dotnet" or "" && TechStackCatalog.IsDotnetProject(p.Csproj)
            || p.StackId == "dotnet"
            || TechStackCatalog.IsDotnetProject(p.Csproj));
        if (hasDotnet || catalog.Projects.Count == 0)
        {
            var sln = FindSolution(catalog.Root);
            if (!string.IsNullOrEmpty(sln))
                targets.Add(sln);
            else
            {
                foreach (var p in catalog.Projects.Where(p => p.IsTest && TechStackCatalog.IsDotnetProject(p.Csproj)))
                {
                    if (File.Exists(p.Csproj))
                        targets.Add(p.Csproj);
                }
            }
        }

        var seenDir = new HashSet<string>(targets.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var p in catalog.Projects)
        {
            if (p.StackId is "dotnet" or "")
                continue;
            var dir = Path.Combine(catalog.Root, p.RelDir.Replace('/', Path.DirectorySeparatorChar));
            var key = Path.GetFullPath(Directory.Exists(dir) ? dir : (File.Exists(p.Csproj) ? Path.GetDirectoryName(p.Csproj)! : dir));
            if (!seenDir.Add(key))
                continue;
            var target = File.Exists(p.Csproj) ? p.Csproj : key;
            targets.Add(target);
        }
        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<(int ExitCode, string Log)> TestAsync(
        string cwd,
        string target,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var plan = StackCommands.PlanTest(cwd, target);
        return await StackCommands.RunAsync(plan, progress, ct).ConfigureAwait(false);
    }

    static string? PickOne(string[] files, string root)
    {
        if (files.Length == 0)
            return null;
        if (files.Length == 1)
            return files[0];
        var folder = Path.GetFileName(root);
        return files.FirstOrDefault(p =>
                   string.Equals(Path.GetFileNameWithoutExtension(p), folder, StringComparison.OrdinalIgnoreCase))
               ?? files[0];
    }
}
