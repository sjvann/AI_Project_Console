using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Tech;
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

    public static IReadOnlyList<ProductLine> ReadProductLines(JsonObject manifest, IReadOnlyList<ServiceEntry>? services = null)
    {
        var list = new List<ProductLine>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var node = manifest["productLines"] ?? manifest["product_lines"] ?? manifest["scanRoots"] ?? manifest["scan_roots"];
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is JsonObject obj)
                {
                    var lineRoot = JsonUtil.Pick(JsonUtil.Str(obj["root"]), JsonUtil.Str(obj["path"]), JsonUtil.Str(obj["dir"])).Replace('\\', '/').TrimEnd('/');
                    if (string.IsNullOrEmpty(lineRoot) || !seen.Add(lineRoot))
                        continue;
                    var label = JsonUtil.Pick(JsonUtil.Str(obj["label"]), JsonUtil.Str(obj["name"]), JsonUtil.Str(obj["group"]), FolderName(lineRoot));
                    var id = JsonUtil.Pick(JsonUtil.Str(obj["id"]), Slug(label));
                    list.Add(new ProductLine(id, string.IsNullOrEmpty(label) ? lineRoot : label, lineRoot));
                }
                else
                {
                    var lineRoot = JsonUtil.Str(item).Replace('\\', '/').TrimEnd('/');
                    if (string.IsNullOrEmpty(lineRoot) || !seen.Add(lineRoot))
                        continue;
                    var label = FolderName(lineRoot);
                    list.Add(new ProductLine(Slug(label), string.IsNullOrEmpty(label) ? lineRoot : label, lineRoot));
                }
            }
        }

        if (list.Count == 0 && services is not null)
        {
            foreach (var svc in services)
            {
                if (!TrySiblingRoot(svc.Project, out var lineRoot) || !seen.Add(lineRoot))
                    continue;
                var label = string.IsNullOrEmpty(svc.Group) ? FolderName(lineRoot) : svc.Group;
                list.Add(new ProductLine(Slug(label), label, lineRoot));
            }
        }
        return list;
    }

    internal static bool TrySiblingRoot(string project, out string lineRoot)
    {
        lineRoot = "";
        var n = project.Replace('\\', '/').Trim();
        if (!n.StartsWith("../", StringComparison.Ordinal))
            return false;
        var parts = n.Split('/');
        if (parts.Length < 2 || parts[0] != ".." || string.IsNullOrEmpty(parts[1]) || parts[1] == "..")
            return false;
        lineRoot = $"../{parts[1]}";
        return true;
    }

    private static string FolderName(string posixPath)
    {
        var n = posixPath.Replace('\\', '/').TrimEnd('/');
        var i = n.LastIndexOf('/');
        return i < 0 ? n : n[(i + 1)..];
    }

    public static ProjectCatalog Build(string root)
    {
        root = Path.GetFullPath(root);
        var manifest = LoadManifest(root);
        var name = JsonUtil.Pick(JsonUtil.Str(manifest["name"]), Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
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
            services = [];

        var lines = ReadProductLines(manifest, services);
        var scan = SkipProjectScan(manifest)
            ? new ScanResult(root, [])
            : ProjectScanner.ScanWorkspace(root, lines);
        var projects = ProjectPurpose.Fill(root, manifest, scan.Projects).ToList();
        var scanned = DedupeIds(ProjectScanner.ExternalServiceCandidates(scan with { Projects = projects }).Select(FromScan).ToList());
        if (services.Count == 0)
            services = scanned;
        else if (MergeScanServices(manifest))
            services = DedupeIds(MergeManifestOverScan(scanned, services));

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
        services = FillIcons(root, services, projects);
        services = FillDescriptions(services, projects);

        var frontend = JsonUtil.Str(manifest["frontend"]);
        if (string.IsNullOrEmpty(frontend))
        {
            var first = services.FirstOrDefault(s => !string.IsNullOrEmpty(s.OpenUrl));
            if (first is not null)
                frontend = first.Id;
        }

        var summary = $"{projects.Count} 專案 · {projects.Count(p => p.IsExecutable)} 可執行 · {services.Count} 服務";
        if (!string.IsNullOrEmpty(scan.Error))
            summary = scan.Error;

        var catalog = new ProjectCatalog
        {
            Root = root,
            Name = name,
            Services = services,
            Projects = projects,
            StartOrder = startOrder,
            Frontend = frontend,
            Manifest = manifest,
            Scan = scan with { Projects = projects },
            Summary = summary,
        };
        ProjectPurpose.TryWrite(catalog);
        return catalog;
    }

    internal static bool SkipProjectScan(JsonObject manifest)
    {
        var node = manifest["scanProjects"] ?? manifest["scan_projects"] ?? manifest["scan"];
        if (node is not JsonValue jv)
            return false;
        if (jv.TryGetValue<bool>(out var flag))
            return !flag;
        var text = JsonUtil.Str(jv);
        return text is "false" or "0" or "no" or "off";
    }

    /// <summary>
    /// 掃描到的服務保留，再把 <c>services</c> 當額外／覆寫（同路徑以清單為準）。
    /// 預設仍是「有 services 就整份取代」，避免 AION 等既有清單被掃進多餘項目。
    /// </summary>
    internal static bool MergeScanServices(JsonObject manifest)
    {
        var node = manifest["mergeScanServices"] ?? manifest["merge_scan_services"] ?? manifest["mergeScan"];
        if (node is not JsonValue jv)
            return false;
        if (jv.TryGetValue<bool>(out var flag))
            return flag;
        var text = JsonUtil.Str(jv);
        return text is "true" or "1" or "yes" or "on";
    }

    internal static List<ServiceEntry> MergeManifestOverScan(
        IReadOnlyList<ServiceEntry> scanned,
        IReadOnlyList<ServiceEntry> listed)
    {
        var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<ServiceEntry>(scanned);
        for (var i = 0; i < merged.Count; i++)
            byPath[ProjectKey(merged[i].Project)] = i;
        foreach (var svc in listed)
        {
            var key = ProjectKey(svc.Project);
            if (byPath.TryGetValue(key, out var idx))
                merged[idx] = svc;
            else
            {
                byPath[key] = merged.Count;
                merged.Add(svc);
            }
        }
        return merged;
    }

    internal static string ProjectKey(string project)
    {
        var n = (project ?? "").Replace('\\', '/').Trim().Trim('/');
        if (n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            n = n[..^".csproj".Length];
        else if (n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
            n = n[..^".fsproj".Length];
        else if (n.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            n = n[..^".vbproj".Length];
        else
        {
            var file = n.Split('/').LastOrDefault() ?? "";
            if (TechStackCatalog.IsExactManifestName(file) && n.Length > file.Length)
                n = n[..^(file.Length)].TrimEnd('/');
        }
        return n.TrimEnd('/');
    }

    public static bool HasOpenableFrontend(ProjectCatalog? catalog) =>
        catalog is not null && catalog.Services.Any(s => !string.IsNullOrEmpty(s.OpenUrl));

    public static bool HasUiOrService(ProjectCatalog? catalog) =>
        catalog is not null && catalog.Services.Count > 0;

    public static bool IsCurrentConsole(ProjectCatalog catalog, ServiceEntry svc, string? processDirectory = null)
    {
        var host = HostService(catalog, svc);
        var projectDir = ResolveProjectDirectory(catalog, host);
        if (projectDir is null)
            return false;
        var procDir = Path.GetFullPath(processDirectory ?? AppContext.BaseDirectory);
        return IsSameOrUnder(procDir, projectDir);
    }

    internal static string? ResolveProjectDirectory(ProjectCatalog catalog, ServiceEntry svc) =>
        ResolveProjectDirectory(catalog.Root, svc.Project);

    internal static string? ResolveProjectDirectory(string root, string project)
    {
        var rel = (project ?? "").Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, rel));
        if (File.Exists(full))
            return Path.GetDirectoryName(full);
        if (Directory.Exists(full))
            return full;
        var csproj = full.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? full : full + ".csproj";
        if (File.Exists(csproj))
            return Path.GetDirectoryName(csproj);
        var manifest = TechStackCatalog.FindPreferredManifest(full);
        return manifest is null ? null : Path.GetDirectoryName(manifest);
    }

    internal static List<ServiceEntry> FillIcons(string root, List<ServiceEntry> services, IReadOnlyList<ProjectInfo> projects)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            if (string.IsNullOrEmpty(p.IconPath) || !byKey.TryAdd(ProjectKey(p.RelDir), p.IconPath))
                continue;
        }
        for (var i = 0; i < services.Count; i++)
        {
            var svc = services[i];
            if (!string.IsNullOrEmpty(svc.IconPath))
            {
                var declared = AppIconLocator.ResolveAbsolute(root, svc.IconPath);
                if (declared is not null)
                {
                    services[i] = svc with { IconPath = AppIconLocator.RelPath(root, declared) };
                    continue;
                }
            }
            if (byKey.TryGetValue(ProjectKey(svc.Project), out var fromProj))
            {
                services[i] = svc with { IconPath = fromProj };
                continue;
            }
            var dir = ResolveProjectDirectory(root, svc.Project);
            var found = dir is null ? null : AppIconLocator.FindRel(root, dir);
            if (!string.IsNullOrEmpty(found))
                services[i] = svc with { IconPath = found };
        }
        return services;
    }

    internal static List<ServiceEntry> FillDescriptions(List<ServiceEntry> services, IReadOnlyList<ProjectInfo> projects)
    {
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in projects)
        {
            var fromProj = ShortPurpose(p.Description);
            if (string.IsNullOrEmpty(fromProj))
                continue;
            byKey.TryAdd(ProjectKey(p.RelDir), fromProj);
        }
        for (var i = 0; i < services.Count; i++)
        {
            var svc = services[i];
            var declared = ShortPurpose(svc.Description);
            if (!string.IsNullOrEmpty(declared) && !SamePurpose(declared, svc.Label))
            {
                services[i] = svc with { Description = declared };
                continue;
            }
            if (byKey.TryGetValue(ProjectKey(svc.Project), out var fromProj) && !SamePurpose(fromProj, svc.Label))
                services[i] = svc with { Description = fromProj };
            else
                services[i] = svc with { Description = "" };
        }
        return services;
    }

    internal static string ShortPurpose(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var s = text.Trim();
        var cut = s.IndexOfAny(['\r', '\n']);
        return cut < 0 ? s : s[..cut].Trim();
    }

    static bool SamePurpose(string purpose, string label) =>
        purpose.Equals(label, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSameOrUnder(string path, string root)
    {
        var a = WithTrailingSep(Path.GetFullPath(path));
        var b = WithTrailingSep(Path.GetFullPath(root));
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithTrailingSep(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

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

    internal static string ResolveOpenPath(ProjectInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.LaunchUrl))
            return info.LaunchUrl.Trim().TrimStart('/');
        return info.IsWebApi ? "scalar" : "";
    }

    private static string GuessGroupFromProject(string project) =>
        string.IsNullOrEmpty(project) ? "其他" : project.Split('/', 2)[0];

    private static ServiceEntry FromScan(ProjectInfo info)
    {
        int? port = info.Ports.Count > 0 ? info.Ports[0] : null;
        var httpUrls = info.ApplicationUrls.Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)).ToList();
        var baseUrl = httpUrls.Count > 0 ? httpUrls[0] : (info.ApplicationUrls.Count > 0 ? info.ApplicationUrls[0] : "");
        var openPath = ResolveOpenPath(info);
        string health, openUrl;
        string? aspnet;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var trimmed = baseUrl.TrimEnd('/');
            health = trimmed + "/health";
            openUrl = string.IsNullOrEmpty(openPath) ? trimmed + "/" : trimmed + "/" + openPath;
            aspnet = string.Join(';', info.ApplicationUrls);
        }
        else if (port is not null)
        {
            health = $"http://127.0.0.1:{port}/health";
            openUrl = string.IsNullOrEmpty(openPath) ? $"http://localhost:{port}/" : $"http://localhost:{port}/{openPath}";
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
            Source: "scan",
            Description: info.Description);
    }

    private static ServiceEntry? FromManifest(JsonObject item)
    {
        var project = JsonUtil.Pick(JsonUtil.Str(item["project"])).Replace('\\', '/');
        if (string.IsNullOrEmpty(project))
            return null;
        var stem = Path.GetFileName(project);
        if (stem.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
            stem = Path.GetFileNameWithoutExtension(stem);
        else if (stem.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || TechStackCatalog.IsExactManifestName(stem))
            stem = Path.GetFileName(project.TrimEnd('/').Contains('/') ? project[..project.LastIndexOf('/')] : project);
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
        var preStart = JsonUtil.Pick(JsonUtil.Str(item["preStart"]), JsonUtil.Str(item["pre_start"]), JsonUtil.Str(item["ensure"]));
        var ready = JsonUtil.Pick(JsonUtil.Str(item["ready"]), JsonUtil.Str(item["readyUrl"]), JsonUtil.Str(item["ready_url"]));
        var icon = JsonUtil.Pick(JsonUtil.Str(item["icon"])).Replace('\\', '/');
        var description = JsonUtil.Str(item["description"]);
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
            PreStart: string.IsNullOrEmpty(preStart) ? null : preStart.Replace('\\', '/'),
            Source: "manifest",
            DependsOn: ReadDependsOn(item),
            Ready: string.IsNullOrEmpty(ready) ? null : ready,
            ReadyTimeoutMs: ReadReadyTimeoutMs(item),
            IconPath: icon,
            Description: description);
    }

    private static IReadOnlyList<ServiceDependency> ReadDependsOn(JsonObject item)
    {
        var node = item["dependsOn"] ?? item["depends_on"];
        if (node is not JsonArray arr || arr.Count == 0)
            return [];
        var list = new List<ServiceDependency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var x in arr)
        {
            string id;
            var optional = false;
            if (x is JsonObject obj)
            {
                id = JsonUtil.Pick(JsonUtil.Str(obj["id"]), JsonUtil.Str(obj["service"]));
                optional = IsTruthy(obj["optional"]);
            }
            else
                id = JsonUtil.Str(x);
            if (string.IsNullOrEmpty(id) || !seen.Add(id))
                continue;
            list.Add(new ServiceDependency(id, optional));
        }
        return list;
    }

    private static int? ReadReadyTimeoutMs(JsonObject item)
    {
        var node = item["readyTimeoutMs"] ?? item["ready_timeout_ms"];
        if (node is JsonValue jv && jv.TryGetValue<int>(out var n) && n > 0)
            return n;
        return int.TryParse(JsonUtil.Str(node), out var parsed) && parsed > 0 ? parsed : null;
    }

    private static bool IsTruthy(JsonNode? node)
    {
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b))
                return b;
            if (jv.TryGetValue<int>(out var n))
                return n != 0;
        }
        var s = JsonUtil.Str(node);
        return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
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
