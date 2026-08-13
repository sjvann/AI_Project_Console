using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Catalog;

public static class ServiceCatalogBuilder
{
    private static readonly Regex NonAlnum = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    public static string Slug(string name)
    {
        var s = NonAlnum.Replace(name, "-").Trim('-').ToLowerInvariant();
        return string.IsNullOrEmpty(s) ? "service" : s;
    }

    public static JsonObject LoadManifest(string root)
    {
        var path = Path.Combine(root, AppInfo.ManifestName);
        return JsonUtil.LoadObject(path);
    }

    public static ProjectCatalog Build(string root)
    {
        root = Path.GetFullPath(root);
        var scan = ProjectScanner.ScanWorkspace(root);
        var manifest = LoadManifest(root);
        var name = JsonUtil.Pick(JsonUtil.Str(manifest["name"]), Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var projects = scan.Projects.ToList();
        List<ServiceEntry> services;
        if (manifest["services"] is JsonArray raw && raw.Count > 0)
        {
            services = [];
            foreach (var item in raw)
            {
                if (item is JsonObject obj)
                {
                    var svc = FromManifest(obj);
                    if (svc is not null)
                        services.Add(svc);
                }
            }
            services = DedupeIds(services);
        }
        else
        {
            services = DedupeIds(ProjectScanner.ExternalServiceCandidates(scan).Select(FromScan).ToList());
        }

        var startOrder = new List<string>();
        var orderNode = manifest["startOrder"] ?? manifest["start_order"];
        if (orderNode is JsonArray arr)
        {
            foreach (var x in arr)
            {
                var s = JsonUtil.Str(x);
                if (!string.IsNullOrEmpty(s))
                    startOrder.Add(s);
            }
        }
        if (startOrder.Count == 0)
            startOrder = services.Where(s => s.HostedBy is null).Select(s => s.Id).ToList();

        var frontend = JsonUtil.Str(manifest["frontend"]);
        if (string.IsNullOrEmpty(frontend))
        {
            var first = services.FirstOrDefault(s => !string.IsNullOrEmpty(s.OpenUrl));
            if (first is not null)
                frontend = first.Id;
        }

        var summary = $"{projects.Count} 專案 · {projects.Count(p => p.IsExecutable)} 可執行 · {services.Count} 對外服務";
        if (!string.IsNullOrEmpty(scan.Error))
            summary = scan.Error;

        return new ProjectCatalog
        {
            Root = root,
            Name = name,
            Services = services,
            Projects = projects,
            StartOrder = startOrder,
            Frontend = frontend,
            Manifest = manifest,
            Scan = scan,
            Summary = summary,
        };
    }

    public static ServiceEntry? ById(ProjectCatalog catalog, string id) =>
        catalog.Services.FirstOrDefault(s => s.Id == id);

    public static ServiceEntry HostService(ProjectCatalog catalog, ServiceEntry svc)
    {
        if (string.IsNullOrEmpty(svc.HostedBy))
            return svc;
        return ById(catalog, svc.HostedBy) ?? svc;
    }

    public static IReadOnlyList<ServiceEntry> OrderedRunnable(ProjectCatalog catalog)
    {
        var byId = catalog.Services.Where(s => s.HostedBy is null).ToDictionary(s => s.Id);
        var ordered = new List<ServiceEntry>();
        var seen = new HashSet<string>();
        foreach (var sid in catalog.StartOrder)
        {
            if (byId.TryGetValue(sid, out var svc) && seen.Add(sid))
                ordered.Add(svc);
        }
        foreach (var svc in catalog.Services)
        {
            if (svc.HostedBy is not null || !seen.Add(svc.Id))
                continue;
            ordered.Add(svc);
        }
        return ordered;
    }

    private static string GuessGroupFromProject(string project) =>
        string.IsNullOrEmpty(project) ? "其他" : project.Split('/', 2)[0];

    private static ServiceEntry FromScan(ProjectInfo info)
    {
        int? port = info.Ports.Count > 0 ? info.Ports[0] : null;
        var httpUrls = info.ApplicationUrls.Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)).ToList();
        var baseUrl = httpUrls.Count > 0 ? httpUrls[0] : (info.ApplicationUrls.Count > 0 ? info.ApplicationUrls[0] : "");
        string health, openUrl;
        string? aspnet;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var trimmed = baseUrl.TrimEnd('/');
            health = trimmed + "/health";
            openUrl = trimmed + (string.IsNullOrEmpty(info.LaunchUrl) ? "/" : "/" + info.LaunchUrl);
            aspnet = string.Join(';', info.ApplicationUrls);
        }
        else if (port is not null)
        {
            health = $"http://127.0.0.1:{port}/health";
            openUrl = $"http://localhost:{port}/";
            aspnet = $"http://localhost:{port}";
        }
        else
        {
            health = openUrl = "";
            aspnet = null;
        }
        return new ServiceEntry(
            Id: Slug(info.Name),
            Label: info.Name,
            Stem: info.Name,
            Project: info.RelDir,
            Port: port,
            Health: health,
            OpenUrl: openUrl,
            Group: info.Group,
            AspnetUrls: aspnet,
            Source: "scan");
    }

    private static ServiceEntry? FromManifest(JsonObject item)
    {
        var project = JsonUtil.Pick(JsonUtil.Str(item["project"])).Replace('\\', '/');
        if (string.IsNullOrEmpty(project))
            return null;
        var stem = Path.GetFileName(project);
        if (stem.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            stem = Path.GetFileNameWithoutExtension(stem);
        var label = JsonUtil.Pick(JsonUtil.Str(item["label"]), stem);
        var sid = JsonUtil.Pick(JsonUtil.Str(item["id"]), Slug(label));
        int? port = null;
        var portRaw = JsonUtil.Str(item["port"]);
        if (int.TryParse(portRaw, out var p))
            port = p;
        else if (item["port"] is JsonValue jv && jv.TryGetValue<int>(out var pi))
            port = pi;
        var health = JsonUtil.Str(item["health"]);
        if (string.IsNullOrEmpty(health) && port is not null)
            health = $"http://127.0.0.1:{port}/health";
        var openUrl = JsonUtil.Pick(JsonUtil.Str(item["openUrl"]), JsonUtil.Str(item["open_url"]));
        var aspnet = JsonUtil.Pick(JsonUtil.Str(item["aspnetUrls"]), JsonUtil.Str(item["aspnet_urls"]), JsonUtil.Str(item["urls"]));
        var hosted = JsonUtil.Pick(JsonUtil.Str(item["hostedBy"]), JsonUtil.Str(item["hosted_by"]));
        if (project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            project = project[..^".csproj".Length];
        return new ServiceEntry(
            Id: sid,
            Label: label,
            Stem: stem,
            Project: project,
            Port: port,
            Health: health,
            OpenUrl: openUrl,
            Group: JsonUtil.Pick(JsonUtil.Str(item["group"]), GuessGroupFromProject(project)),
            HostedBy: string.IsNullOrEmpty(hosted) ? null : hosted,
            AspnetUrls: string.IsNullOrEmpty(aspnet) ? null : aspnet,
            Source: "manifest");
    }

    private static List<ServiceEntry> DedupeIds(List<ServiceEntry> services)
    {
        var seen = new Dictionary<string, int>();
        var outList = new List<ServiceEntry>();
        foreach (var svc in services)
        {
            var n = seen.GetValueOrDefault(svc.Id);
            seen[svc.Id] = n + 1;
            outList.Add(n == 0 ? svc : svc with { Id = $"{svc.Id}-{n + 1}" });
        }
        return outList;
    }
}
