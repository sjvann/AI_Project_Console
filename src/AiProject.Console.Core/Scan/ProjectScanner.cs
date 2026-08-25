using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AiProject.Console.Core.Scan;

public static class ProjectScanner
{
    private static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", "node_modules", ".ai_project", ".ai_house", "packages",
    };

    private static readonly Regex UrlRe = new(@"https?://[^\s;]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> ApiDocPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Scalar.AspNetCore",
        "Microsoft.AspNetCore.OpenApi",
        "Swashbuckle.AspNetCore",
    };

    public static IReadOnlyList<string> ListCsprojPaths(string root)
    {
        root = Path.GetFullPath(root);
        var found = new List<string>();
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
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(60_000);
                if (proc.ExitCode == 0 && output.Length > 0)
                {
                    foreach (var rel in output.Split('\0'))
                    {
                        if (!rel.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var path = Path.GetFullPath(Path.Combine(root, rel));
                        if (File.Exists(path))
                            found.Add(path);
                    }
                }
            }
        }
        catch (Exception)
        {
            found.Clear();
        }

        if (found.Count == 0)
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            {
                var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Any(p => SkipDirNames.Contains(p)))
                    continue;
                found.Add(Path.GetFullPath(path));
            }
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
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
        return new ScanResult(root, projects);
    }

    private static void AddFrom(
        string scanRoot,
        string workspaceRoot,
        string? groupOverride,
        List<ProjectInfo> projects,
        HashSet<string> seen)
    {
        if (!Directory.Exists(scanRoot))
            return;
        foreach (var csproj in ListCsprojPaths(scanRoot))
        {
            if (!seen.Add(csproj))
                continue;
            var info = ScanProject(csproj, workspaceRoot);
            if (!string.IsNullOrWhiteSpace(groupOverride))
                info = info with { Group = groupOverride };
            projects.Add(info);
        }
    }

    public static IReadOnlyList<ProjectInfo> ExternalServiceCandidates(ScanResult scan) =>
        scan.Projects.Where(p => p.IsExecutable && (p.Ports.Count > 0 || p.ApplicationUrls.Count > 0)).ToList();

    public static ProjectInfo ScanProject(string csproj, string root)
    {
        var projectDir = Path.GetDirectoryName(csproj)!;
        var (sdk, outputType, isExe, isWeb, hasApiDocs, hasUiMarkers) = ParseCsprojMeta(csproj);
        var (urls, ports, launchUrl) = ParseLaunchSettings(projectDir);
        var relDir = RelPosix(root, projectDir);
        var name = Path.GetFileNameWithoutExtension(csproj);
        var parts = relDir.ToLowerInvariant().Split('/');
        var isTest = parts.Contains("tests")
            || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase);
        var isWebApi = isWeb
            && !hasUiMarkers
            && !NameLooksLikeUiWeb(name)
            && !HasRazorFiles(projectDir)
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
            Language: DetectLanguage(csproj));
    }

    public static string DetectLanguage(string projectFile)
    {
        var ext = Path.GetExtension(projectFile);
        return ext.ToLowerInvariant() switch
        {
            ".csproj" => "C#",
            ".fsproj" => "F#",
            ".vbproj" => "VB.NET",
            ".vcxproj" => "C++",
            ".esproj" or ".njsproj" => "JavaScript",
            ".tsproj" => "TypeScript",
            ".pyproj" => "Python",
            _ => "",
        };
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

    private static (string Sdk, string OutputType, bool IsExe, bool IsWeb, bool HasApiDocs, bool HasUiMarkers) ParseCsprojMeta(string csproj)
    {
        try
        {
            var doc = XDocument.Load(csproj);
            var root = doc.Root;
            if (root is null)
                return ("", "Library", false, false, false, false);
            var sdk = (string?)root.Attribute("Sdk") ?? "";
            var isWeb = sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal);
            var outputType = "Library";
            var hasApiDocs = false;
            var hasUiMarkers = false;
            foreach (var elem in root.Descendants())
            {
                var local = LocalName(elem.Name);
                if (local == "OutputType" && !string.IsNullOrWhiteSpace(elem.Value) && outputType == "Library")
                    outputType = elem.Value.Trim();
                if (local == "PackageReference")
                {
                    var include = (string?)elem.Attribute("Include") ?? "";
                    if (ApiDocPackages.Contains(include))
                        hasApiDocs = true;
                    if (include.Contains("Components.Web", StringComparison.OrdinalIgnoreCase)
                        || include.Contains("Blazor", StringComparison.OrdinalIgnoreCase))
                        hasUiMarkers = true;
                }
                if (local.StartsWith("Blazor", StringComparison.OrdinalIgnoreCase))
                    hasUiMarkers = true;
            }
            var isExe = outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase)
                || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
                || isWeb;
            return (sdk.Trim(), outputType, isExe, isWeb, hasApiDocs, hasUiMarkers);
        }
        catch (Exception)
        {
            return ("", "Library", false, false, false, false);
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
