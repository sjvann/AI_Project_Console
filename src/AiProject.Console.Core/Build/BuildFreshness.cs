using System.Xml.Linq;
using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Build;

public static class BuildFreshness
{
    private static readonly HashSet<string> SourceSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".razor", ".cshtml", ".csproj", ".props", ".targets", ".json", ".js", ".css",
        ".html", ".proto", ".resx",
    };

    /// <summary>
    /// Runtime profile / config files. <c>dotnet build</c> does not rewrite the assembly when
    /// these change, so they must not count as compile sources.
    /// </summary>
    public static bool IsRuntimeConfig(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Live process state that is rewritten while the service runs (e.g. EDDSS
    /// <c>data/twins.json</c>). Counting these as sources leaves「需重編」stuck
    /// after a successful compile, because the files keep getting newer than the DLL.
    /// </summary>
    public static bool IsRuntimeState(string path)
    {
        if (IsRuntimeConfig(path))
            return true;
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => p.Equals("logs", StringComparison.OrdinalIgnoreCase)
            || p.Equals("App_Data", StringComparison.OrdinalIgnoreCase)))
            return true;
        return parts.Any(p => p.Equals("data", StringComparison.OrdinalIgnoreCase))
            && Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    public static string AssemblyName(string projectDir)
    {
        var csproj = Directory.Exists(projectDir)
            ? Directory.GetFiles(projectDir, "*.csproj").OrderBy(p => p).FirstOrDefault()
            : null;
        if (csproj is null)
            return Path.GetFileName(projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        try
        {
            var root = XDocument.Load(csproj).Root;
            if (root is not null)
            {
                foreach (var elem in root.Descendants())
                {
                    if (elem.Name.LocalName == "AssemblyName" && !string.IsNullOrWhiteSpace(elem.Value))
                        return elem.Value.Trim();
                }
            }
        }
        catch { /* ignore */ }
        return Path.GetFileNameWithoutExtension(csproj);
    }

    public static (double Mtime, string? Path) BuildOutput(string projectDir)
    {
        var name = AssemblyName(projectDir);
        var binDir = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binDir))
            return (0, null);
        double best = 0;
        string? bestPath = null;
        foreach (var dll in Directory.EnumerateFiles(binDir, name + ".dll", SearchOption.AllDirectories))
        {
            var mtime = File.GetLastWriteTimeUtc(dll).Subtract(DateTime.UnixEpoch).TotalSeconds;
            if (mtime > best)
            {
                best = mtime;
                bestPath = dll;
            }
        }
        return (best, bestPath);
    }

    public static (double Mtime, string? Path) NewestSource(string projectDir)
    {
        double best = 0;
        string? bestPath = null;
        if (!Directory.Exists(projectDir))
            return (0, null);
        foreach (var path in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is "bin" or "obj"))
                continue;
            if (!SourceSuffixes.Contains(Path.GetExtension(path)))
                continue;
            if (IsRuntimeState(path))
                continue;
            var mtime = File.GetLastWriteTimeUtc(path).Subtract(DateTime.UnixEpoch).TotalSeconds;
            if (mtime > best)
            {
                best = mtime;
                bestPath = path;
            }
        }
        return (best, bestPath);
    }

    public static string ProjectDirForService(ProjectCatalog catalog, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        var path = Path.Combine(catalog.Root, host.Project.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Path.GetDirectoryName(path)! : path;
    }

    public static BuildState ServiceBuildState(ProjectCatalog catalog, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        var projectDir = ProjectDirForService(catalog, host);
        var (srcMtime, _) = NewestSource(projectDir);
        var (outMtime, outPath) = BuildOutput(projectDir);
        var status = outMtime <= 0 ? "unbuilt" : srcMtime > outMtime ? "stale" : "fresh";
        return new BuildState(
            Id: svc.Id,
            Name: svc.Label,
            Status: status,
            Path: host.Project,
            Output: outPath ?? "",
            Label: svc.Label);
    }

    public static IReadOnlyList<BuildState> AllServiceBuildStates(ProjectCatalog catalog)
    {
        var seen = new HashSet<string>();
        var list = new List<BuildState>();
        foreach (var svc in catalog.Services)
        {
            var host = ServiceCatalogBuilder.HostService(catalog, svc);
            if (!seen.Add(host.Id))
                continue;
            list.Add(ServiceBuildState(catalog, host));
        }
        return list;
    }

    public static BuildState ProjectBuildState(string root, ProjectInfo info)
    {
        var projectDir = Path.Combine(root, info.RelDir.Replace('/', Path.DirectorySeparatorChar));
        var (srcMtime, _) = NewestSource(projectDir);
        var (outMtime, outPath) = BuildOutput(projectDir);
        var status = outMtime <= 0 ? "unbuilt" : srcMtime > outMtime ? "stale" : "fresh";
        var kind = info.IsTest ? "測試" : (info.IsExecutable || info.IsWeb ? "核心" : "函式庫");
        return new BuildState(
            Id: info.RelDir,
            Name: info.Name,
            Status: status,
            Path: info.RelDir,
            System: info.Group,
            Kind: kind,
            Output: outPath ?? "");
    }

    public static IReadOnlyList<BuildState> AllProjectBuildStates(ProjectCatalog catalog)
    {
        var kindOrder = new Dictionary<string, int> { ["核心"] = 0, ["函式庫"] = 1, ["測試"] = 2 };
        return catalog.Projects
            .Select(p => ProjectBuildState(catalog.Root, p))
            .OrderBy(s => s.System)
            .ThenBy(s => kindOrder.GetValueOrDefault(s.Kind, 9))
            .ThenBy(s => s.Name)
            .ToList();
    }
}
