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
        return targets;
    }
}
