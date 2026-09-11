using System.Text.Json;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tech;

public static class TechStackDetector
{
    static readonly string[] PythonEntries =
        ["main.py", "app.py", "manage.py", "wsgi.py", "asgi.py", "__main__.py"];

    static readonly string[] NodeEntries =
        ["index.js", "index.ts", "index.mjs", "server.js", "app.js", "main.ts", "main.js"];

    /// <summary>
    /// Node 應用程式進入點。不含 wwwroot 常見的 <c>app.js</c>／靜態 <c>main.js</c>。
    /// </summary>
    static readonly string[] NodeAppEntries =
        ["index.js", "index.ts", "index.mjs", "server.js"];

    public static IReadOnlyList<string> StackIdsFromProjects(IEnumerable<ProjectInfo> projects) =>
        projects
            .Select(p => p.StackId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static IReadOnlyList<string> RequiredStacks(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return [];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in StackIdsFromProjects(catalog.Projects))
            ids.Add(id);
        if (catalog.Scan.StackIds is { Count: > 0 } extra)
        {
            foreach (var id in extra)
                ids.Add(id);
        }
        foreach (var svc in catalog.Services)
        {
            var path = Path.Combine(catalog.Root, (svc.Project ?? "").Replace('/', Path.DirectorySeparatorChar));
            var id = StackIdForPath(path);
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }
        return ids.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string StackIdForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        try
        {
            if (File.Exists(path))
            {
                if (path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                    return "python";
                var fromFile = TechStackCatalog.MatchFile(path);
                if (fromFile is not null)
                    return fromFile.Id;
                var fromSrc = TechStackCatalog.MatchSourceExtension(path);
                if (fromSrc is not null)
                    return fromSrc.Id;
                path = Path.GetDirectoryName(path) ?? path;
            }
            if (Directory.Exists(path))
            {
                var manifest = TechStackCatalog.FindPreferredManifest(path);
                if (manifest is not null)
                    return TechStackCatalog.MatchFile(manifest)?.Id ?? "";
                if (TechStackCatalog.FindSolutionFile(path) is not null)
                    return "dotnet";
                if (DirectoryLooksLikePythonProject(path))
                    return "python";
                if (DirectoryLooksLikeNodeProject(path))
                    return "node";
            }
        }
        catch (Exception)
        {
            return "";
        }
        return "";
    }

    public static string DisplayLanguage(string projectFile, string projectDir)
    {
        var mapped = TechStackCatalog.LanguageForDotnetSuffix(projectFile);
        if (!string.IsNullOrEmpty(mapped) && TechStackCatalog.IsDotnetProject(projectFile))
            return mapped;
        if (Path.GetExtension(projectFile).Equals(".tsproj", StringComparison.OrdinalIgnoreCase))
            return "TypeScript";
        if (Path.GetExtension(projectFile).Equals(".vcxproj", StringComparison.OrdinalIgnoreCase))
            return "C++";
        var stack = TechStackCatalog.MatchFile(projectFile);
        if (stack is null)
            return mapped;
        if (stack.Id == "node" && LooksLikeTypeScript(projectDir))
            return "TypeScript";
        if (stack.Id is "java-maven" or "java-gradle" && HasExtension(projectDir, ".kt"))
            return "Kotlin";
        if (stack.Id == "dart")
        {
            try
            {
                var text = File.ReadAllText(projectFile);
                if (TechStackCatalog.LooksLikeFlutter(text))
                    return "Flutter";
            }
            catch (Exception) { /* ignore */ }
        }
        return stack.Language;
    }

    public static bool LooksLikeTypeScript(string projectDir) =>
        File.Exists(Path.Combine(projectDir, "tsconfig.json"))
        || HasExtension(projectDir, ".ts")
        || HasExtension(projectDir, ".tsx");

    public static bool HasExtension(string projectDir, string ext)
    {
        try
        {
            return Directory.EnumerateFiles(projectDir, "*" + ext, SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsPythonEntry(string fileName) =>
        PythonEntries.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    public static bool IsNodeEntry(string fileName) =>
        NodeEntries.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    public static bool DirectoryLooksLikePythonProject(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*.py", SearchOption.TopDirectoryOnly)
                .Any(f => IsPythonEntry(Path.GetFileName(f)));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool DirectoryLooksLikeNodeProject(string dir)
    {
        if (File.Exists(Path.Combine(dir, "package.json")))
            return true;
        try
        {
            return Directory.GetFiles(dir)
                .Any(f => NodeAppEntries.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string PackageManager(string projectDir)
    {
        if (File.Exists(Path.Combine(projectDir, "pnpm-lock.yaml")) && CliUtil.CommandExists("pnpm"))
            return "pnpm";
        if (File.Exists(Path.Combine(projectDir, "yarn.lock")) && CliUtil.CommandExists("yarn"))
            return "yarn";
        if ((File.Exists(Path.Combine(projectDir, "bun.lockb")) || File.Exists(Path.Combine(projectDir, "bun.lock")))
            && CliUtil.CommandExists("bun"))
            return "bun";
        return "npm";
    }

    public static IReadOnlyList<string> NpmScripts(string projectDir)
    {
        var path = Path.Combine(projectDir, "package.json");
        if (!File.Exists(path))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
                return [];
            return scripts.EnumerateObject().Select(p => p.Name).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string? PackageJsonName(string projectDir)
    {
        var path = Path.Combine(projectDir, "package.json");
        if (!File.Exists(path))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                return n.GetString();
        }
        catch (Exception) { /* ignore */ }
        return null;
    }

    public static int? PortFromDotEnv(string projectDir)
    {
        foreach (var name in new[] { ".env", ".env.development", ".env.local" })
        {
            var path = Path.Combine(projectDir, name);
            if (!File.Exists(path))
                continue;
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("PORT=", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = line[5..].Trim().Trim('"').Trim('\'');
                        if (int.TryParse(value, out var port) && port > 0)
                            return port;
                    }
                }
            }
            catch (Exception) { /* ignore */ }
        }
        return null;
    }

    public static bool HasGoMain(string projectDir)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(projectDir, "*.go", SearchOption.TopDirectoryOnly).Take(30))
            {
                var text = File.ReadAllText(file);
                if (text.Contains("package main", StringComparison.Ordinal)
                    && text.Contains("func main", StringComparison.Ordinal))
                    return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
        return false;
    }
}
