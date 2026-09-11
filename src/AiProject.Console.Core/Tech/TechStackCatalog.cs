namespace AiProject.Console.Core.Tech;

/// <summary>
/// 市場常見語言／建置系統。專案檔優先，其次才用副檔名推斷。
/// </summary>
public static class TechStackCatalog
{
    public static readonly IReadOnlyList<ToolSpec> Tools =
    [
        new("dotnet", ".NET SDK", ["dotnet"],
            "Microsoft.DotNet.SDK.10",
            "https://dotnet.microsoft.com/download",
            "安裝 .NET SDK 後重新開控制台。官方：https://dotnet.microsoft.com/download"),
        new("python", "Python", ["py", "python", "python3"],
            "Python.Python.3.12",
            "https://www.python.org/downloads/",
            "安裝時請勾選 Add python.exe to PATH。官方：https://www.python.org/downloads/"),
        new("node", "Node.js", ["node"],
            "OpenJS.NodeJS.LTS",
            "https://nodejs.org/",
            "安裝 LTS 版 Node.js（含 npm）。官方：https://nodejs.org/"),
        new("go", "Go", ["go"],
            "GoLang.Go",
            "https://go.dev/dl/",
            "安裝 Go 工具鏈。官方：https://go.dev/dl/"),
        new("cargo", "Rust（Cargo）", ["cargo"],
            "Rustlang.Rust.MSVC",
            "https://rustup.rs/",
            "安裝 Rust MSVC 工具鏈（含 cargo）。官方：https://rustup.rs/"),
        new("java", "Java JDK", ["java", "javac"],
            "Microsoft.OpenJDK.21",
            "https://learn.microsoft.com/java/openjdk/download",
            "安裝 JDK 21 或更新。官方：https://learn.microsoft.com/java/openjdk/download"),
        new("mvn", "Maven", ["mvn"],
            "Apache.Maven",
            "https://maven.apache.org/download.cgi",
            "安裝 Apache Maven，或在專案放 mvnw。https://maven.apache.org/download.cgi"),
        new("php", "PHP", ["php"],
            "PHP.PHP.8.3",
            "https://windows.php.net/download/",
            "安裝 PHP 並加入 PATH。官方：https://windows.php.net/download/"),
        new("composer", "Composer", ["composer"],
            "Composer.Composer",
            "https://getcomposer.org/download/",
            "安裝 Composer。官方：https://getcomposer.org/download/"),
        new("ruby", "Ruby", ["ruby"],
            "RubyInstallerTeam.Ruby.3.3",
            "https://rubyinstaller.org/",
            "安裝 RubyInstaller。官方：https://rubyinstaller.org/"),
        new("cmake", "CMake", ["cmake"],
            "Kitware.CMake",
            "https://cmake.org/download/",
            "安裝 CMake。官方：https://cmake.org/download/"),
        new("dart", "Dart／Flutter", ["dart", "flutter"],
            "Google.FlutterSDK",
            "https://docs.flutter.dev/get-started/install",
            "安裝 Flutter SDK（含 Dart）。官方：https://docs.flutter.dev/get-started/install"),
    ];

    public static readonly IReadOnlyList<TechStackProfile> Stacks =
    [
        new("dotnet", "C#",
            ExactFileNames: [],
            FileSuffixes: [".csproj", ".fsproj", ".vbproj"],
            SourceExtensions: [".cs", ".razor", ".cshtml", ".fs", ".vb"],
            RequiredToolIds: ["dotnet"],
            ManifestRank: 0,
            KeepAllInDirectory: true),
        new("node", "JavaScript",
            ExactFileNames: ["package.json"],
            FileSuffixes: [".esproj", ".njsproj", ".tsproj"],
            SourceExtensions: [".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".vue", ".svelte"],
            RequiredToolIds: ["node"],
            ManifestRank: 0),
        new("python", "Python",
            ExactFileNames: ["pyproject.toml", "Pipfile", "setup.py", "requirements.txt", "environment.yml"],
            FileSuffixes: [".pyproj"],
            SourceExtensions: [".py", ".pyw"],
            RequiredToolIds: ["python"],
            ManifestRank: 10,
            SkipIfSameDirHas: null),
        new("go", "Go",
            ExactFileNames: ["go.mod"],
            FileSuffixes: [],
            SourceExtensions: [".go"],
            RequiredToolIds: ["go"],
            ManifestRank: 0),
        new("rust", "Rust",
            ExactFileNames: ["Cargo.toml"],
            FileSuffixes: [],
            SourceExtensions: [".rs"],
            RequiredToolIds: ["cargo"],
            ManifestRank: 0),
        new("java-maven", "Java",
            ExactFileNames: ["pom.xml"],
            FileSuffixes: [],
            SourceExtensions: [".java", ".kt"],
            RequiredToolIds: ["java", "mvn"],
            ManifestRank: 0),
        new("java-gradle", "Java",
            ExactFileNames: ["build.gradle", "build.gradle.kts"],
            FileSuffixes: [],
            SourceExtensions: [".java", ".kt"],
            RequiredToolIds: ["java"],
            ManifestRank: 5,
            SkipIfSameDirHas: ["pom.xml"]),
        new("php", "PHP",
            ExactFileNames: ["composer.json"],
            FileSuffixes: [],
            SourceExtensions: [".php"],
            RequiredToolIds: ["php"],
            ManifestRank: 0),
        new("ruby", "Ruby",
            ExactFileNames: ["Gemfile"],
            FileSuffixes: [],
            SourceExtensions: [".rb"],
            RequiredToolIds: ["ruby"],
            ManifestRank: 0),
        new("cpp-msbuild", "C++",
            ExactFileNames: [],
            FileSuffixes: [".vcxproj"],
            SourceExtensions: [".c", ".cc", ".cpp", ".cxx", ".h", ".hpp"],
            RequiredToolIds: ["cmake"],
            ManifestRank: 0,
            KeepAllInDirectory: true),
        new("cpp-cmake", "C++",
            ExactFileNames: ["CMakeLists.txt"],
            FileSuffixes: [],
            SourceExtensions: [".c", ".cc", ".cpp", ".cxx", ".h", ".hpp"],
            RequiredToolIds: ["cmake"],
            ManifestRank: 10,
            SkipIfSameDirHas: []),
        new("dart", "Dart",
            ExactFileNames: ["pubspec.yaml"],
            FileSuffixes: [],
            SourceExtensions: [".dart"],
            RequiredToolIds: ["dart"],
            ManifestRank: 0),
    ];

    static readonly Dictionary<string, ToolSpec> ToolsById =
        Tools.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> ExactNames = new(
        Stacks.SelectMany(s => s.ExactFileNames),
        StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> Suffixes = new(
        Stacks.SelectMany(s => s.FileSuffixes),
        StringComparer.OrdinalIgnoreCase);

    static readonly HashSet<string> SourceExt = new(
        Stacks.SelectMany(s => s.SourceExtensions),
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> SourceExtensions => SourceExt;

    public static bool IsProjectManifest(string pathOrRel)
    {
        var name = Path.GetFileName(pathOrRel);
        if (string.IsNullOrEmpty(name))
            return false;
        if (ExactNames.Contains(name))
            return true;
        return Suffixes.Contains(Path.GetExtension(name));
    }

    public static bool IsExactManifestName(string fileName) =>
        !string.IsNullOrEmpty(fileName) && ExactNames.Contains(fileName);

    public static bool IsSourceFile(string pathOrRel)
    {
        var ext = Path.GetExtension(pathOrRel);
        return !string.IsNullOrEmpty(ext) && SourceExt.Contains(ext);
    }

    public static ToolSpec? Tool(string id) =>
        ToolsById.TryGetValue(id, out var spec) ? spec : null;

    public static TechStackProfile? MatchFile(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name))
            return null;
        TechStackProfile? byName = null;
        TechStackProfile? byExt = null;
        foreach (var stack in Stacks)
        {
            if (stack.ExactFileNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                if (byName is null || stack.ManifestRank < byName.ManifestRank)
                    byName = stack;
            }
            var ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext)
                && stack.FileSuffixes.Any(s => s.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                byExt ??= stack;
        }
        return byName ?? byExt;
    }

    public static TechStackProfile? MatchSourceExtension(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
            return null;
        return Stacks.FirstOrDefault(s =>
            s.SourceExtensions.Any(x => x.Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }

    public static IReadOnlyList<string> PickManifests(IEnumerable<string> files)
    {
        var list = files.Where(f => MatchFile(f) is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0)
            return [];

        var names = new HashSet<string>(
            list.Select(f => Path.GetFileName(f) ?? ""),
            StringComparer.OrdinalIgnoreCase);
        list.RemoveAll(f =>
        {
            var stack = MatchFile(f);
            return stack is not null
                && stack.SkipIfSameDirHasNames.Any(n => names.Contains(n));
        });

        var keep = new List<string>();
        foreach (var group in list.GroupBy(f => MatchFile(f)!.Id, StringComparer.OrdinalIgnoreCase))
        {
            var stack = MatchFile(group.First())!;
            if (stack.KeepAllInDirectory)
            {
                keep.AddRange(group);
                continue;
            }
            var best = group
                .OrderBy(f => PythonRank(f, stack))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .First();
            keep.Add(best);
        }
        keep.Sort(StringComparer.OrdinalIgnoreCase);
        return keep;
    }

    static int PythonRank(string path, TechStackProfile stack)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.Equals("setup.py", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (name.Equals("environment.yml", StringComparison.OrdinalIgnoreCase))
            return 4;
        return stack.ManifestRank;
    }

    public static bool IsDotnetProject(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSolutionFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FindSolutionFile(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        try
        {
            var slnx = Directory.GetFiles(directory, "*.slnx");
            if (slnx.Length > 0)
                return slnx.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First();
            var sln = Directory.GetFiles(directory, "*.sln");
            return sln.Length > 0
                ? sln.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).First()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string? FindPreferredManifest(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        try
        {
            var files = Directory.GetFiles(directory);
            var picked = PickManifests(files);
            if (picked.Count == 0)
                return null;
            var dotnet = picked.FirstOrDefault(IsDotnetProject);
            return dotnet ?? picked[0];
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> RequiredToolIdsFor(IEnumerable<string> stackIds)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in stackIds)
        {
            var stack = Stacks.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (stack is null)
                continue;
            foreach (var tool in stack.RequiredToolIds)
            {
                if (seen.Add(tool))
                    ids.Add(tool);
            }
        }
        return ids;
    }

    public static string LanguageForDotnetSuffix(string projectFile)
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
            _ => MatchFile(projectFile)?.Language ?? "",
        };
    }

    static readonly string[] NodeWebHints =
    [
        "express", "fastify", "koa", "next", "nuxt", "vite", "react", "vue", "angular",
        "svelte", "@angular/core", "webpack", "hono",
    ];

    static readonly string[] PythonWebHints =
    [
        "flask", "django", "fastapi", "starlette", "tornado", "uvicorn", "quart",
    ];

    public static bool LooksLikeNodeWeb(string packageJsonText)
    {
        var lower = packageJsonText.ToLowerInvariant();
        return NodeWebHints.Any(h => lower.Contains(h, StringComparison.Ordinal));
    }

    public static bool LooksLikePythonWeb(string text)
    {
        var lower = text.ToLowerInvariant();
        return PythonWebHints.Any(h => lower.Contains(h, StringComparison.Ordinal));
    }

    public static bool LooksLikeFlutter(string pubspecText) =>
        pubspecText.Contains("flutter:", StringComparison.Ordinal);
}
