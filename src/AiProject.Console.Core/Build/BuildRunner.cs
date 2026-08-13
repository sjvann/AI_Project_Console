using System.Diagnostics;
using System.Text;
using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Build;

public static class BuildRunner
{
    public static async Task<(int ExitCode, string Log)> BuildAsync(
        string cwd,
        string target,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var lines = new List<string>();
        void Emit(string line)
        {
            lines.Add(line);
            progress?.Report(line);
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("minimal");
        psi.ArgumentList.Add("--nologo");

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var combined = (await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
        foreach (var line in combined.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length > 0)
                Emit(text);
        }
        return (proc.ExitCode, string.Join('\n', lines));
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
