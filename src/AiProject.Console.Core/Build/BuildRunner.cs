using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Tech;

namespace AiProject.Console.Core.Build;

public static class BuildRunner
{
    public static async Task<(int ExitCode, string Log)> BuildAsync(
        string cwd,
        string target,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var plan = StackCommands.PlanBuild(cwd, target);
        return await StackCommands.RunAsync(plan, progress, ct).ConfigureAwait(false);
    }

    public static IReadOnlyList<string> TargetsFor(ProjectCatalog catalog, string handler)
    {
        var targets = new List<string>();
        if (handler == "build_services")
        {
            var seen = new HashSet<string>();
            foreach (var svc in catalog.Services)
            {
                var host = ServiceCatalogBuilder.HostService(catalog, svc);
                if (!seen.Add(host.Id))
                    continue;
                targets.Add(Path.Combine(catalog.Root, host.Project.Replace('/', Path.DirectorySeparatorChar)));
            }
        }
        else if (handler == "build_stale")
        {
            foreach (var state in BuildFreshness.AllServiceBuildStates(catalog))
            {
                if (state.Status is "stale" or "unbuilt")
                    targets.Add(Path.Combine(catalog.Root, state.Path.Replace('/', Path.DirectorySeparatorChar)));
            }
            foreach (var state in BuildFreshness.AllProjectBuildStates(catalog))
            {
                if (state.Status is not ("stale" or "unbuilt"))
                    continue;
                var path = Path.Combine(catalog.Root, state.Path.Replace('/', Path.DirectorySeparatorChar));
                if (!targets.Contains(path))
                    targets.Add(path);
            }
        }
        else
        {
            foreach (var p in catalog.Projects)
                targets.Add(Path.Combine(catalog.Root, p.RelDir.Replace('/', Path.DirectorySeparatorChar)));
        }
        return Canonicalize(catalog.Root, targets);
    }

    /// <summary>
    /// 略過沒有專案檔、不能編譯的靜態資料夾；同一工作目錄只編一次。
    /// </summary>
    public static IReadOnlyList<string> Canonicalize(string workspaceRoot, IReadOnlyList<string> targets)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var plan = StackCommands.PlanBuild(workspaceRoot, target);
            if (plan.MissingToolIds.Count > 0)
            {
                if (seen.Add(Path.GetFullPath(target)))
                    result.Add(target);
                continue;
            }
            if (string.IsNullOrEmpty(plan.FileName))
                continue;
            var key = string.IsNullOrEmpty(plan.WorkingDirectory)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(plan.WorkingDirectory);
            if (!seen.Add(key))
                continue;
            result.Add(plan.WorkingDirectory);
        }
        return result;
    }
}
