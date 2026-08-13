using System.Text.RegularExpressions;
using System.Xml.Linq;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public readonly record struct SemVer(int Major, int Minor, int Patch, string PreRelease, string Prefix)
{
    public bool HasPreRelease => !string.IsNullOrEmpty(PreRelease);

    public string ToTag()
    {
        var core = $"{Major}.{Minor}.{Patch}";
        return string.IsNullOrEmpty(PreRelease) ? Prefix + core : Prefix + core + "-" + PreRelease;
    }

    public override string ToString() => ToTag();
}

public static class ReleaseVersion
{
    private static readonly Regex SemVerRe = new(
        @"^(?<v>v)?(?<maj>\d+)\.(?<min>\d+)(?:\.(?<pat>\d+))?(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CsVersionRe = new(
        @"(?:const|static).*\bVersion\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex PyVersionRe = new(
        @"^__version__\s*=\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TomlVersionRe = new(
        @"^version\s*=\s*['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".ai_project", "dist", "publish", ".vs",
    };

    public static bool TryParse(string? text, out SemVer ver)
    {
        ver = default;
        var raw = (text ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return false;
        var m = SemVerRe.Match(raw);
        if (!m.Success)
            return false;
        var patch = m.Groups["pat"].Success ? int.Parse(m.Groups["pat"].Value) : 0;
        var prefix = m.Groups["v"].Success ? "v" : "";
        ver = new SemVer(
            int.Parse(m.Groups["maj"].Value),
            int.Parse(m.Groups["min"].Value),
            patch,
            m.Groups["pre"].Success ? m.Groups["pre"].Value : "",
            prefix);
        return true;
    }

    public static string NormalizeTag(string? text)
    {
        var raw = (text ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return "";
        if (TryParse(raw, out var ver))
            return string.IsNullOrEmpty(ver.Prefix) ? "v" + ver.ToTag() : ver.ToTag();
        return raw.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? raw : "v" + raw.TrimStart('v', 'V');
    }

    public static SemVer Bump(SemVer ver, string part)
    {
        var kind = (part ?? "").Trim().ToLowerInvariant();
        if (ver.HasPreRelease && kind is "patch" or "")
            return ver with { PreRelease = "" };
        return kind switch
        {
            "major" => new SemVer(ver.Major + 1, 0, 0, "", ver.Prefix),
            "minor" => new SemVer(ver.Major, ver.Minor + 1, 0, "", ver.Prefix),
            _ => new SemVer(ver.Major, ver.Minor, ver.Patch + 1, "", ver.Prefix),
        };
    }

    public static string SuggestNext(string? latest, string part = "patch")
    {
        if (!TryParse(latest, out var ver))
            return "v0.1.0";
        if (string.IsNullOrEmpty(ver.Prefix))
            ver = ver with { Prefix = "v" };
        return Bump(ver, part).ToTag();
    }

    public static string SuggestTag(string? githubLatest, string? gitTag, string? projectVersion)
    {
        if (TryParse(githubLatest, out _))
            return SuggestNext(githubLatest);
        if (!string.IsNullOrWhiteSpace(gitTag) && TryParse(gitTag, out _))
            return NormalizeTag(gitTag);
        if (!string.IsNullOrWhiteSpace(projectVersion) && TryParse(projectVersion, out _))
            return NormalizeTag(projectVersion);
        return "v0.1.0";
    }

    public static bool IsValidTag(string? tag)
    {
        var t = (tag ?? "").Trim();
        return t.Length > 0
            && t.IndexOfAny([' ', '\t', '\r', '\n', '\\']) < 0
            && t.IndexOf("..", StringComparison.Ordinal) < 0;
    }

    public static string DetectProjectVersion(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return "";
        foreach (var path in RootCandidates(root))
        {
            var v = FromFile(path);
            if (!string.IsNullOrEmpty(v))
                return v;
        }
        foreach (var path in EnumerateShallow(root, "*.csproj", 3, 40))
        {
            var v = FromXmlVersion(path);
            if (!string.IsNullOrEmpty(v))
                return v;
        }
        foreach (var path in EnumerateShallow(root, "AppInfo.cs", 4, 8))
        {
            var v = FromCSharpVersion(path);
            if (!string.IsNullOrEmpty(v))
                return v;
        }
        return "";
    }

    private static IEnumerable<string> RootCandidates(string root)
    {
        yield return Path.Combine(root, "Directory.Build.props");
        yield return Path.Combine(root, "package.json");
        yield return Path.Combine(root, "pyproject.toml");
        yield return Path.Combine(root, "__init__.py");
        yield return Path.Combine(root, "version.txt");
    }

    private static string FromFile(string path)
    {
        if (!File.Exists(path))
            return "";
        var name = Path.GetFileName(path);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            var obj = JsonUtil.LoadObject(path);
            return JsonUtil.Str(obj["version"]);
        }
        if (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            return FirstMatch(File.ReadAllText(path), TomlVersionRe);
        if (name.Equals("__init__.py", StringComparison.OrdinalIgnoreCase))
            return FirstMatch(File.ReadAllText(path), PyVersionRe);
        if (name.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
            return File.ReadAllText(path).Trim().Split('\n', 2)[0].Trim();
        if (name.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            return FromXmlVersion(path);
        return "";
    }

    private static string FromXmlVersion(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            string? Pick(string local)
            {
                var el = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName.Equals(local, StringComparison.OrdinalIgnoreCase));
                var text = el?.Value?.Trim() ?? "";
                return string.IsNullOrEmpty(text) || text.Contains('$') ? null : text;
            }
            return Pick("Version") ?? Pick("PackageVersion") ?? Pick("VersionPrefix") ?? "";
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            return "";
        }
    }

    private static string FromCSharpVersion(string path)
    {
        try
        {
            return FirstMatch(File.ReadAllText(path), CsVersionRe);
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static string FirstMatch(string text, Regex re)
    {
        var m = re.Match(text);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static IEnumerable<string> EnumerateShallow(string root, string pattern, int maxDepth, int maxFiles)
    {
        var found = 0;
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0 && found < maxFiles)
        {
            var (dir, depth) = queue.Dequeue();
            string[] files;
            try { files = Directory.GetFiles(dir, pattern); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var f in files)
            {
                yield return f;
                if (++found >= maxFiles)
                    yield break;
            }
            if (depth >= maxDepth)
                continue;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var sub in subs)
            {
                if (SkipDirs.Contains(Path.GetFileName(sub)))
                    continue;
                queue.Enqueue((sub, depth + 1));
            }
        }
    }
}
