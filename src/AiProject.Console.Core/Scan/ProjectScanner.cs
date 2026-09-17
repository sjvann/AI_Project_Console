using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AiProject.Console.Core.Tech;

namespace AiProject.Console.Core.Scan;

public static class ProjectScanner
{
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", ".ai_project", ".ai_house", "packages",
        "venv", ".venv", "__pycache__", "target", "dist", "vendor", ".gradle", ".next",
        ".nuxt", ".tox", ".mypy_cache", ".pytest_cache", "bower_components", ".dart_tool",
        "coverage", ".hg", ".svn", "build", "wwwroot",
    };

    private static readonly Regex UrlRe = new(@"https?://[^\s;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ApiDocPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Scalar.AspNetCore",
        "Microsoft.AspNetCore.OpenApi",
        "Swashbuckle.AspNetCore",
    };

    public static IReadOnlyList<string> ListCsprojPaths(string root) =>
        ListProjectFiles(root).Where(TechStackCatalog.IsDotnetProject).ToList();

    public static IReadOnlyList<string> ListProjectFiles(string root)
    {
        root = Path.GetFullPath(root);
        var found = new List<string>();
        var gitRels = TryGitLsFiles(root);
        if (gitRels is not null)
        {
            foreach (var rel in gitRels)
            {
                if (!TechStackCatalog.IsProjectManifest(rel))
                    continue;
                var path = Path.GetFullPath(Path.Combine(root, rel));
                if (File.Exists(path) && !IsSkippedPath(path))
                    found.Add(path);
            }
        }

        if (found.Count == 0)
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (IsSkippedPath(path) || !TechStackCatalog.IsProjectManifest(path))
                        continue;
                    found.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception)
            {
                // ignore
            }
        }

        var picked = new List<string>();
        foreach (var group in found.GroupBy(p => Path.GetDirectoryName(p) ?? p, StringComparer.OrdinalIgnoreCase))
            picked.AddRange(TechStackCatalog.PickManifests(group));
        picked.Sort(StringComparer.OrdinalIgnoreCase);
        return picked;
    }

    static IReadOnlyList<string>? TryGitLsFiles(string root)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "ls-files -c -o --exclude-standard -z")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(60_000);
            if (proc.ExitCode != 0 || output.Length == 0)
                return null;
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static bool IsSkippedPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => SkipDirNames.Contains(p));
    }

    public static ScanResult ScanWorkspace(string root, IReadOnlyList<ProductLine>? extraRoots = null)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            return new ScanResult(root, [], $"目錄不存在：{root}");

        var projects = new List<ProjectInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFrom(root, root, groupOverride: null, projects, seen);
        if (extraRoots is not null)
        {
            foreach (var line in extraRoots)
            {
                if (string.IsNullOrWhiteSpace(line.Root))
                    continue;
                var scanRoot = Path.GetFullPath(Path.Combine(root, line.Root.Replace('/', Path.DirectorySeparatorChar)));
                if (string.Equals(scanRoot, root, StringComparison.OrdinalIgnoreCase))
                    continue;
                AddFrom(scanRoot, root, line.Label, projects, seen);
            }
        }
        return new ScanResult(root, projects, "", StackIdsFrom(projects));
    }

    static IReadOnlyList<string> StackIdsFrom(IEnumerable<ProjectInfo> projects) =>
        projects.Select(p => p.StackId).Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void AddFrom(
        string scanRoot,
        string workspaceRoot,
        string? groupOverride,
        List<ProjectInfo> projects,
        HashSet<string> seen)
    {
        if (!Directory.Exists(scanRoot))
            return;
        foreach (var file in ListProjectFiles(scanRoot))
        {
            if (!seen.Add(file))
                continue;
            var info = ScanManifest(file, workspaceRoot);
            if (!string.IsNullOrWhiteSpace(groupOverride))
                info = info with { Group = groupOverride };
            projects.Add(info);
        }
        AddExtensionOnly(scanRoot, workspaceRoot, groupOverride, projects, seen);
    }

    public static IReadOnlyList<ProjectInfo> ExternalServiceCandidates(ScanResult scan) =>
        scan.Projects.Where(p =>
            p.IsExecutable
            && (p.IsUi || p.Ports.Count > 0 || p.ApplicationUrls.Count > 0)).ToList();

    public static ProjectInfo ScanManifest(string projectFile, string root)
    {
        if (TechStackCatalog.IsDotnetProject(projectFile))
            return ScanProject(projectFile, root);
        return ScanOther(projectFile, root);
    }

    public static ProjectInfo ScanProject(string csproj, string root)
    {
        var projectDir = Path.GetDirectoryName(csproj)!;
        var (sdk, outputType, isExe, isWeb, hasApiDocs, hasUiMarkers, applicationIcon, description) = ParseCsprojMeta(csproj);
        var (urls, ports, launchUrl) = ParseLaunchSettings(projectDir);
        var relDir = RelPosix(root, projectDir);
        var name = Path.GetFileNameWithoutExtension(csproj);
        var parts = relDir.ToLowerInvariant().Split('/');
        var isTest = parts.Contains("tests")
            || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase);
        var hasRazor = HasRazorFiles(projectDir);
        var isUi = !isTest && isExe && (
            hasUiMarkers
            || hasRazor
            || NameLooksLikeUiWeb(name));
        var isWebApi = isWeb
            && !hasUiMarkers
            && !NameLooksLikeUiWeb(name)
            && !hasRazor
            && (hasApiDocs || NameLooksLikeApi(name));
        return new ProjectInfo(
            RelDir: relDir,
            Name: name,
            Csproj: csproj,
            Sdk: sdk,
            OutputType: outputType,
            IsExecutable: isExe && !isTest,
            IsWeb: isWeb,
            IsWebApi: isWebApi,
            IsTest: isTest,
            Ports: ports,
            ApplicationUrls: urls,
            LaunchUrl: launchUrl,
            Group: GuessGroup(relDir),
            Language: DetectLanguage(csproj),
            IsUi: isUi,
            IconPath: AppIconLocator.FindRel(root, projectDir, applicationIcon) ?? "",
            StackId: "dotnet",
            Description: description);
    }

    public static string DetectLanguage(string projectFile) =>
        TechStackCatalog.LanguageForDotnetSuffix(projectFile);

    static ProjectInfo ScanOther(string projectFile, string root)
    {
        var projectDir = Path.GetDirectoryName(projectFile) ?? projectFile;
        var stack = TechStackCatalog.MatchFile(projectFile);
        var stackId = stack?.Id ?? "";
        var relDir = RelPosix(root, projectDir);
        var name = TechStackDetector.PackageJsonName(projectDir)
            ?? (TechStackCatalog.IsExactManifestName(Path.GetFileName(projectFile))
                ? Path.GetFileName(projectDir)
                : Path.GetFileNameWithoutExtension(projectFile));
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(projectDir);
        var language = TechStackDetector.DisplayLanguage(projectFile, projectDir);
        var isTest = relDir.ToLowerInvariant().Split('/').Contains("tests")
            || name.Contains("test", StringComparison.OrdinalIgnoreCase);
        var (isExe, isWeb, isUi, ports) = InferMeta(projectFile, projectDir, stackId);
        var urls = ports.Select(p => $"http://localhost:{p}").ToList();
        return new ProjectInfo(
            RelDir: relDir,
            Name: name,
            Csproj: projectFile,
            Sdk: stackId,
            OutputType: isExe ? "Exe" : "Library",
            IsExecutable: isExe && !isTest,
            IsWeb: isWeb,
            IsWebApi: isWeb && !isUi,
            IsTest: isTest,
            Ports: ports,
            ApplicationUrls: urls,
            LaunchUrl: "",
            Group: GuessGroup(relDir),
            Language: language,
            IsUi: isUi && !isTest,
            IconPath: AppIconLocator.FindRel(root, projectDir) ?? "",
            StackId: stackId);
    }

    static (bool IsExe, bool IsWeb, bool IsUi, List<int> Ports) InferMeta(string projectFile, string projectDir, string stackId)
    {
        var port = TechStackDetector.PortFromDotEnv(projectDir);
        var ports = port is int p ? new List<int> { p } : new List<int>();
        try
        {
            if (stackId == "node")
            {
                var scripts = TechStackDetector.NpmScripts(projectDir);
                var hasStart = scripts.Any(s => s.Equals("start", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("dev", StringComparison.OrdinalIgnoreCase));
                var text = File.Exists(projectFile) ? File.ReadAllText(projectFile) : "";
                var web = TechStackCatalog.LooksLikeNodeWeb(text);
                return (hasStart || web, web, web, ports);
            }
            if (stackId == "python")
            {
                var text = File.Exists(projectFile) ? File.ReadAllText(projectFile) : "";
                var web = TechStackCatalog.LooksLikePythonWeb(text);
                var entry = TechStackDetector.DirectoryLooksLikePythonProject(projectDir);
                return (entry || web, web, false, ports);
            }
            if (stackId == "go")
                return (TechStackDetector.HasGoMain(projectDir), false, false, ports);
            if (stackId == "rust")
            {
                var text = File.ReadAllText(projectFile);
                var exe = text.Contains("[package]", StringComparison.Ordinal);
                return (exe, false, false, ports);
            }
            if (stackId is "java-maven" or "java-gradle")
            {
                var text = File.ReadAllText(projectFile);
                var web = text.Contains("spring-boot", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("war", StringComparison.OrdinalIgnoreCase);
                return (web || text.Contains("mainClass", StringComparison.OrdinalIgnoreCase), web, false, ports);
            }
            if (stackId == "php")
                return (Directory.Exists(Path.Combine(projectDir, "public")), true, true, ports);
            if (stackId == "ruby")
            {
                var rails = File.Exists(Path.Combine(projectDir, "bin", "rails"));
                return (rails, rails, rails, ports);
            }
            if (stackId == "dart")
            {
                var flutter = TechStackCatalog.LooksLikeFlutter(File.ReadAllText(projectFile));
                return (true, flutter, flutter, ports);
            }
        }
        catch (Exception)
        {
            return (false, false, false, ports);
        }
        return (false, false, false, ports);
    }

    static void AddExtensionOnly(
        string scanRoot,
        string workspaceRoot,
        string? groupOverride,
        List<ProjectInfo> projects,
        HashSet<string> seen)
    {
        var covered = new List<(string StackId, string Dir)>();
        foreach (var p in projects)
        {
            if (string.IsNullOrEmpty(p.StackId))
                continue;
            var dir = Path.GetFullPath(Path.Combine(workspaceRoot, p.RelDir.Replace('/', Path.DirectorySeparatorChar)));
            covered.Add((p.StackId, dir));
        }

        IEnumerable<string> files;
        var git = TryGitLsFiles(scanRoot);
        if (git is not null)
        {
            files = git
                .Where(TechStackCatalog.IsSourceFile)
                .Select(rel => Path.GetFullPath(Path.Combine(scanRoot, rel)))
                .Where(File.Exists);
        }
        else
        {
            try
            {
                files = Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories)
                    .Where(p => !IsSkippedPath(p) && TechStackCatalog.IsSourceFile(p));
            }
            catch (Exception)
            {
                return;
            }
        }

        var byStackDir = new Dictionary<(string Stack, string Dir), int>();
        foreach (var path in files)
        {
            if (IsSkippedPath(path))
                continue;
            var stack = TechStackCatalog.MatchSourceExtension(path);
            if (stack is null)
                continue;
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir))
                continue;
            if (covered.Any(c => NestedUnderProject(c.Dir, dir) || SameStackCovered(c.StackId, c.Dir, stack.Id, dir)))
                continue;
            var key = (stack.Id, dir);
            byStackDir[key] = byStackDir.GetValueOrDefault(key) + 1;
        }

        foreach (var ((stackId, dir), count) in byStackDir)
        {
            if (!DirectoryLooksLikeLooseProject(dir, stackId, count))
                continue;
            if (TechStackCatalog.FindSolutionFile(dir) is not null)
                continue;
            if (projects.Any(p => SameProjectDirectory(workspaceRoot, p.RelDir, dir)))
                continue;
            if (!seen.Add(dir))
                continue;
            projects.Add(LooseProject(dir, workspaceRoot, stackId, groupOverride));
        }
    }

    static bool SameStackCovered(string coveredStack, string coveredDir, string stackId, string dir)
    {
        if (!coveredStack.Equals(stackId, StringComparison.OrdinalIgnoreCase)
            && !(coveredStack.StartsWith("java", StringComparison.Ordinal) && stackId.StartsWith("java", StringComparison.Ordinal)))
            return false;
        var a = coveredDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return b.StartsWith(a, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
    }

    static bool NestedUnderProject(string coveredDir, string dir)
    {
        var a = coveredDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var b = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    static bool SameProjectDirectory(string workspaceRoot, string relDir, string dir)
    {
        var existing = Path.GetFullPath(Path.Combine(workspaceRoot, relDir.Replace('/', Path.DirectorySeparatorChar)));
        return string.Equals(
            existing.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    static bool DirectoryLooksLikeLooseProject(string dir, string stackId, int fileCount)
    {
        if (stackId == "python")
            return TechStackDetector.DirectoryLooksLikePythonProject(dir);
        if (stackId == "node")
            return File.Exists(Path.Combine(dir, "package.json"))
                || (fileCount >= 8 && TechStackDetector.DirectoryLooksLikeNodeProject(dir));
        if (stackId == "go")
            return TechStackDetector.HasGoMain(dir) || fileCount >= 3;
        if (stackId == "dotnet")
            return false;
        return fileCount >= 8;
    }

    static ProjectInfo LooseProject(string projectDir, string root, string stackId, string? groupOverride)
    {
        var relDir = RelPosix(root, projectDir);
        var language = TechStackCatalog.Stacks.FirstOrDefault(s => s.Id == stackId)?.Language ?? "";
        if (stackId == "node" && TechStackDetector.LooksLikeTypeScript(projectDir))
            language = "TypeScript";
        var port = TechStackDetector.PortFromDotEnv(projectDir);
        var ports = port is int p ? new List<int> { p } : new List<int>();
        var isPy = stackId == "python";
        var isNode = stackId == "node";
        var isExe = (isPy && TechStackDetector.DirectoryLooksLikePythonProject(projectDir))
            || (isNode && TechStackDetector.DirectoryLooksLikeNodeProject(projectDir))
            || (stackId == "go" && TechStackDetector.HasGoMain(projectDir));
        return new ProjectInfo(
            RelDir: relDir,
            Name: Path.GetFileName(projectDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Csproj: projectDir,
            Sdk: stackId,
            OutputType: isExe ? "Exe" : "Library",
            IsExecutable: isExe,
            IsWeb: isNode,
            IsWebApi: false,
            IsTest: relDir.ToLowerInvariant().Split('/').Contains("tests"),
            Ports: ports,
            ApplicationUrls: ports.Select(x => $"http://localhost:{x}").ToList(),
            LaunchUrl: "",
            Group: string.IsNullOrWhiteSpace(groupOverride) ? GuessGroup(relDir) : groupOverride,
            Language: language,
            IsUi: isNode,
            IconPath: AppIconLocator.FindRel(root, projectDir) ?? "",
            StackId: stackId);
    }

    private static string RelPosix(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path)).Replace('\\', '/');
        }
        catch (Exception)
        {
            return path.Replace('\\', '/');
        }
    }

    private static string GuessGroup(string relDir)
    {
        var top = relDir.Split('/', 2)[0];
        return string.IsNullOrEmpty(top) ? "其他" : top;
    }

    private static string LocalName(XName name) => name.LocalName;

    private static string LastNameSegment(string name)
    {
        var i = name.LastIndexOf('.');
        return i < 0 ? name : name[(i + 1)..];
    }

    internal static bool NameLooksLikeApi(string name) =>
        LastNameSegment(name).Equals("Api", StringComparison.OrdinalIgnoreCase);

    internal static bool NameLooksLikeUiWeb(string name)
    {
        var last = LastNameSegment(name);
        return last.Equals("Web", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Frontend", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Blazor", StringComparison.OrdinalIgnoreCase)
            || last.Equals("Ui", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRazorFiles(string projectDir)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(projectDir, "*.razor", SearchOption.AllDirectories))
            {
                var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => SkipDirNames.Contains(p)))
                    continue;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
        return false;
    }

    private static (string Sdk, string OutputType, bool IsExe, bool IsWeb, bool HasApiDocs, bool HasUiMarkers, string ApplicationIcon, string Description) ParseCsprojMeta(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            var root = doc.Root;
            if (root is null)
                return ("", "Library", false, false, false, false, "", "");
            var sdk = (string?)root.Attribute("Sdk") ?? "";
            var isWeb = sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal);
            var outputType = "Library";
            var hasApiDocs = false;
            var hasUiMarkers = false;
            var applicationIcon = "";
            var description = "";
            foreach (var elem in root.Descendants())
            {
                var local = LocalName(elem.Name);
                if (local == "OutputType" && !string.IsNullOrWhiteSpace(elem.Value) && outputType == "Library")
                    outputType = elem.Value.Trim();
                if (local == "ApplicationIcon" && string.IsNullOrEmpty(applicationIcon) && !string.IsNullOrWhiteSpace(elem.Value))
                    applicationIcon = elem.Value.Trim();
                if (local == "Description" && string.IsNullOrEmpty(description) && !string.IsNullOrWhiteSpace(elem.Value))
                    description = elem.Value.Trim();
                if (local == "PackageReference")
                {
                    var include = (string?)elem.Attribute("Include") ?? "";
                    if (ApiDocPackages.Contains(include))
                        hasApiDocs = true;
                    if (include.Contains("Components.Web", StringComparison.OrdinalIgnoreCase)
                        || include.Contains("Blazor", StringComparison.OrdinalIgnoreCase)
                        || include.Contains("Photino", StringComparison.OrdinalIgnoreCase))
                        hasUiMarkers = true;
                }
                if (local.StartsWith("Blazor", StringComparison.OrdinalIgnoreCase))
                    hasUiMarkers = true;
            }
            var isExe = outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
                || isWeb;
            return (sdk.Trim(), outputType, isExe, isWeb, hasApiDocs, hasUiMarkers, applicationIcon, description);
        }
        catch (Exception)
        {
            return ("", "Library", false, false, false, false, "", "");
        }
    }

    private static (List<string> Urls, List<int> Ports, string LaunchUrl) ParseLaunchSettings(string projectDir)
    {
        var path = Path.Combine(projectDir, "Properties", "launchSettings.json");
        if (!File.Exists(path))
            return ([], [], "");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("profiles", out var profiles) || profiles.ValueKind != JsonValueKind.Object)
                return ([], [], "");

            var ranked = profiles.EnumerateObject()
                .OrderBy(p => ProfileRank(p.Name))
                .ToList();
            var urls = new List<string>();
            var ports = new List<int>();
            var launchUrl = "";
            foreach (var profile in ranked)
            {
                if (profile.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (string.IsNullOrEmpty(launchUrl)
                    && profile.Value.TryGetProperty("launchUrl", out var lu)
                    && lu.ValueKind == JsonValueKind.String)
                    launchUrl = lu.GetString()?.Trim() ?? "";
                var raw = "";
                if (profile.Value.TryGetProperty("applicationUrl", out var au) && au.ValueKind == JsonValueKind.String)
                    raw = au.GetString() ?? "";
                else if (profile.Value.TryGetProperty("applicationUrls", out var aus) && aus.ValueKind == JsonValueKind.String)
                    raw = aus.GetString() ?? "";
                if (string.IsNullOrEmpty(raw))
                    continue;
                foreach (Match m in UrlRe.Matches(raw))
                {
                    var match = m.Value;
                    if (match.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        continue;
                    urls.Add(match.TrimEnd('/'));
                    if (Uri.TryCreate(match, UriKind.Absolute, out var uri))
                    {
                        if (uri.Port > 0)
                            ports.Add(uri.Port);
                        else if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                            ports.Add(80);
                    }
                }
            }
            return (urls.Distinct().ToList(), ports.Distinct().ToList(), launchUrl);
        }
        catch (Exception)
        {
            return ([], [], "");
        }
    }

    private static int ProfileRank(string name)
    {
        var key = name.ToLowerInvariant();
        if (key is "http" or "development")
            return 0;
        if (key.Contains("http") && !key.Contains("https"))
            return 1;
        if (key == "https")
            return 9;
        return 5;
    }
}
