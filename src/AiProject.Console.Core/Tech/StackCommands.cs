using System.Diagnostics;
using System.Text;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tech;

public static class StackCommands
{
    public static string StackIdFor(string path) => TechStackDetector.StackIdForPath(path);

    public static ProcessPlan PlanBuild(string workspaceRoot, string target)
    {
        var (dir, file, stackId) = Resolve(workspaceRoot, target);
        return stackId switch
        {
            "dotnet" => Need("dotnet", dir, ["build", file ?? dir, "-v", "minimal", "--nologo"], "dotnet build"),
            "node" => PlanNode(dir, "build"),
            "python" => PlanPythonCompile(dir),
            "go" => Need("go", dir, ["build", "./..."], "go build"),
            "rust" => Need("cargo", dir, ["build"], "cargo build"),
            "java-maven" => PlanMaven(dir, ["-q", "-DskipTests", "compile"]),
            "java-gradle" => PlanGradle(dir, ["build", "-x", "test", "-q"]),
            "php" => PlanPhpRestore(dir),
            "ruby" => PlanRubyRestore(dir),
            "cpp-cmake" => PlanCMake(dir),
            "cpp-msbuild" => Need("dotnet", dir, ["build", file ?? dir, "-v", "minimal"], "dotnet build"),
            "dart" => PlanDartBuild(dir, file),
            _ => PlanUnknown(dir, file),
        };
    }

    public static ProcessPlan PlanTest(string workspaceRoot, string target)
    {
        var (dir, file, stackId) = Resolve(workspaceRoot, target);
        return stackId switch
        {
            "dotnet" => Need("dotnet", dir == workspaceRoot ? workspaceRoot : dir,
                ["test", file ?? target, "-v", "minimal", "--nologo"], "dotnet test"),
            "node" => PlanNode(dir, "test"),
            "python" => PlanPythonTest(dir),
            "go" => Need("go", dir, ["test", "./..."], "go test"),
            "rust" => Need("cargo", dir, ["test"], "cargo test"),
            "java-maven" => PlanMaven(dir, ["-q", "test"]),
            "java-gradle" => PlanGradle(dir, ["test", "-q"]),
            "php" => Need("php", dir, ["vendor/bin/phpunit"], "phpunit"),
            "ruby" => PlanRubyTest(dir),
            "dart" => PlanDartTest(dir),
            _ => PlanBuild(workspaceRoot, target),
        };
    }

    public static ProcessPlan PlanRestore(string workspaceRoot, string target)
    {
        var (dir, file, stackId) = Resolve(workspaceRoot, target);
        return stackId switch
        {
            "dotnet" => Need("dotnet", dir, ["restore", file ?? dir], "dotnet restore"),
            "node" => PlanNodeInstall(dir),
            "python" => PlanPythonRestore(dir),
            "go" => Need("go", dir, ["mod", "download"], "go mod download"),
            "rust" => Need("cargo", dir, ["fetch"], "cargo fetch"),
            "java-maven" => PlanMaven(dir, ["-q", "dependency:resolve"]),
            "java-gradle" => PlanGradle(dir, ["dependencies", "-q"]),
            "php" => PlanPhpRestore(dir),
            "ruby" => PlanRubyRestore(dir),
            "dart" => PlanDartRestore(dir),
            _ => PlanUnknown(dir, file),
        };
    }

    public static ProcessPlan PlanStart(string workingHint, string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        var dir = File.Exists(full) ? Path.GetDirectoryName(full)! : full;
        var stackId = TechStackDetector.StackIdForPath(full);
        if (File.Exists(full) && full.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            return PlanPythonScript(full);
        return stackId switch
        {
            "node" => PlanNodeStart(dir),
            "python" => PlanPythonStart(dir),
            "go" => Need("go", dir, ["run", "."], "go run ."),
            "rust" => Need("cargo", dir, ["run"], "cargo run"),
            "java-maven" => PlanMaven(dir, ["-q", "spring-boot:run"]),
            "java-gradle" => PlanGradle(dir, ["bootRun"]),
            "php" => PlanPhpStart(dir),
            "ruby" => PlanRubyStart(dir),
            "dart" => PlanDartStart(dir),
            "dotnet" => PlanDotnetRun(workingHint, full),
            _ => PlanDotnetRun(workingHint, full),
        };
    }

    public static bool PackagesRestored(string projectDir, string stackId)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return false;
        return stackId switch
        {
            "node" => Directory.Exists(Path.Combine(projectDir, "node_modules")),
            "python" => Directory.Exists(Path.Combine(projectDir, ".venv"))
                || Directory.Exists(Path.Combine(projectDir, "venv")),
            "php" => Directory.Exists(Path.Combine(projectDir, "vendor")),
            "ruby" => Directory.Exists(Path.Combine(projectDir, "vendor")),
            "rust" => Directory.Exists(Path.Combine(projectDir, "target")),
            "java-maven" => Directory.Exists(Path.Combine(projectDir, "target")),
            "go" => File.Exists(Path.Combine(projectDir, "go.sum")),
            "dart" => Directory.Exists(Path.Combine(projectDir, ".dart_tool")),
            _ => true,
        };
    }

    public static async Task<(int ExitCode, string Log)> RunAsync(
        ProcessPlan plan,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        if (plan.MissingToolIds.Count > 0)
        {
            var msg = ToolchainBootstrap.FormatMissing(plan.MissingToolIds);
            progress?.Report(msg);
            return (1, msg);
        }

        if (string.IsNullOrEmpty(plan.FileName))
        {
            var skip = string.IsNullOrWhiteSpace(plan.Display) ? "無需編譯" : plan.Display;
            progress?.Report(skip);
            return (0, skip);
        }

        var lines = new List<string>();
        void Emit(string line)
        {
            lines.Add(line);
            progress?.Report(line);
        }

        Emit(plan.Display + "  (cwd " + plan.WorkingDirectory + ")");
        var psi = new ProcessStartInfo(plan.FileName)
        {
            WorkingDirectory = plan.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in plan.Arguments)
            psi.ArgumentList.Add(a);
        if (plan.ExtraEnv is not null)
        {
            foreach (var kv in plan.ExtraEnv)
                psi.Environment[kv.Key] = kv.Value;
        }

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var combined = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
            foreach (var line in combined.Split('\n'))
            {
                var text = line.TrimEnd('\r');
                if (text.Length > 0)
                    Emit(text);
            }
            return (proc.ExitCode, string.Join('\n', lines));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            Emit(ex.Message);
            return (1, string.Join('\n', lines));
        }
    }

    static (string Dir, string? File, string StackId) Resolve(string workspaceRoot, string target)
    {
        var full = Path.IsPathRooted(target)
            ? Path.GetFullPath(target)
            : Path.GetFullPath(Path.Combine(workspaceRoot, target.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(full))
        {
            var dir = Path.GetDirectoryName(full)!;
            var id = TechStackCatalog.IsDotnetProject(full) || TechStackCatalog.IsSolutionFile(full)
                ? "dotnet"
                : TechStackCatalog.MatchFile(full)?.Id
                  ?? TechStackDetector.StackIdForPath(full);
            return (dir, full, id ?? "");
        }
        if (Directory.Exists(full))
        {
            var manifest = TechStackCatalog.FindPreferredManifest(full);
            string id;
            if (manifest is not null)
            {
                id = TechStackCatalog.IsDotnetProject(manifest) || TechStackCatalog.IsSolutionFile(manifest)
                    ? "dotnet"
                    : TechStackCatalog.MatchFile(manifest)?.Id ?? "";
            }
            else
                id = TechStackDetector.StackIdForPath(full);
            if (string.IsNullOrEmpty(id) && Directory.GetFiles(full, "*.csproj").Length > 0)
                id = "dotnet";
            if (id == "dotnet" && (manifest is null || !TechStackCatalog.IsDotnetProject(manifest)))
            {
                var sln = TechStackCatalog.FindSolutionFile(full);
                if (sln is not null && (manifest is null || TechStackCatalog.IsSolutionFile(sln)))
                    return (full, sln, "dotnet");
            }
            if (string.IsNullOrEmpty(id) && TechStackCatalog.FindSolutionFile(full) is { } foundSln)
                return (full, foundSln, "dotnet");
            return (full, manifest, id ?? "");
        }
        return (workspaceRoot, full, "");
    }

    static ProcessPlan Need(string toolId, string dir, IReadOnlyList<string> args, string display)
    {
        var exe = ToolchainBootstrap.ResolveCommand(toolId);
        if (exe is null)
            return new("", args, dir, [toolId], display);
        return new(exe, args, dir, [], display);
    }

    static ProcessPlan PlanUnknown(string dir, string? file)
    {
        if (file is not null
            && (TechStackCatalog.IsDotnetProject(file) || TechStackCatalog.IsSolutionFile(file)))
            return Need("dotnet", dir, ["build", file, "-v", "minimal", "--nologo"], "dotnet build");
        var csproj = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.csproj").OrderBy(p => p).FirstOrDefault()
            : null;
        if (csproj is not null)
            return Need("dotnet", dir, ["build", csproj, "-v", "minimal", "--nologo"], "dotnet build");
        var sln = Directory.Exists(dir) ? TechStackCatalog.FindSolutionFile(dir) : null;
        if (sln is not null)
            return Need("dotnet", dir, ["build", sln, "-v", "minimal", "--nologo"], "dotnet build");
        return new("", [], dir, [], "無需編譯（沒有專案或方案檔）");
    }

    static ProcessPlan PlanDotnetRun(string workspaceRoot, string projectPath)
    {
        var exe = ToolchainBootstrap.ResolveCommand("dotnet");
        var rel = projectPath;
        try { rel = Path.GetRelativePath(workspaceRoot, projectPath); }
        catch (Exception) { /* keep */ }
        var args = new[] { "run", "--project", rel, "--no-launch-profile" };
        if (exe is null)
            return new("dotnet", args, workspaceRoot, ["dotnet"], "dotnet run");
        return new(exe, args, workspaceRoot, [], "dotnet run");
    }

    static ProcessPlan PlanNode(string dir, string scriptKind)
    {
        var pm = TechStackDetector.PackageManager(dir);
        var scripts = TechStackDetector.NpmScripts(dir);
        if (scriptKind == "build")
        {
            if (!File.Exists(Path.Combine(dir, "package.json")))
                return new("", [], dir, [], "無需編譯（沒有 package.json）");
            if (scripts.Any(s => s.Equals("build", StringComparison.OrdinalIgnoreCase)))
                return NeedNode(pm, dir, ["run", "build"], pm + " run build");
            return PlanNodeInstall(dir);
        }
        if (scriptKind == "test")
        {
            var test = scripts.FirstOrDefault(s => s.Equals("test", StringComparison.OrdinalIgnoreCase));
            if (test is not null)
                return NeedNode(pm, dir, ["run", "test"], pm + " test");
            return new(pm, ["test"], dir, [], pm + " test");
        }
        return PlanNodeInstall(dir);
    }

    static ProcessPlan PlanNodeInstall(string dir) =>
        NeedNode(TechStackDetector.PackageManager(dir), dir, ["install"], TechStackDetector.PackageManager(dir) + " install");

    static ProcessPlan PlanNodeStart(string dir)
    {
        var pm = TechStackDetector.PackageManager(dir);
        var scripts = TechStackDetector.NpmScripts(dir);
        if (scripts.Any(s => s.Equals("dev", StringComparison.OrdinalIgnoreCase)))
            return NeedNode(pm, dir, ["run", "dev"], pm + " run dev");
        if (scripts.Any(s => s.Equals("start", StringComparison.OrdinalIgnoreCase)))
            return NeedNode(pm, dir, ["start"], pm + " start");
        return NeedNode(pm, dir, ["start"], pm + " start");
    }

    static ProcessPlan NeedNode(string pm, string dir, IReadOnlyList<string> args, string display)
    {
        var missing = new List<string>();
        if (!ToolchainBootstrap.IsInstalled("node"))
            missing.Add("node");
        var exe = CliUtil.FindOnPath(pm);
        if (exe is null)
            missing.Add("node");
        if (missing.Count > 0)
            return new(pm, args, dir, missing.Distinct().ToList(), display);
        return new(exe!, args, dir, [], display);
    }

    static ProcessPlan PlanPythonCompile(string dir)
    {
        var py = ToolchainBootstrap.ResolveCommand("python");
        if (py is null)
            return new("", ["-m", "compileall", "-q", "."], dir, ["python"], "python -m compileall");
        var args = new List<string>();
        if (IsPyLauncher(py))
            args.Add("-3");
        args.AddRange(["-m", "compileall", "-q", "."]);
        return new(py, args, dir, [], "python -m compileall");
    }

    static ProcessPlan PlanPythonTest(string dir)
    {
        var py = ToolchainBootstrap.ResolveCommand("python");
        if (py is null)
            return new("", ["-m", "pytest"], dir, ["python"], "pytest");
        var args = new List<string>();
        if (IsPyLauncher(py))
            args.Add("-3");
        if (File.Exists(Path.Combine(dir, "pytest.ini"))
            || File.Exists(Path.Combine(dir, "pyproject.toml"))
            || Directory.Exists(Path.Combine(dir, "tests")))
            args.AddRange(["-m", "pytest"]);
        else
            args.AddRange(["-m", "unittest", "discover"]);
        return new(py, args, dir, [], "python tests");
    }

    static ProcessPlan PlanPythonRestore(string dir)
    {
        var py = ToolchainBootstrap.ResolveCommand("python");
        if (py is null)
            return new("", [], dir, ["python"], "pip install");
        var args = new List<string>();
        if (IsPyLauncher(py))
            args.Add("-3");
        if (File.Exists(Path.Combine(dir, "poetry.lock")) && CliUtil.CommandExists("poetry"))
            return new(CliUtil.FindOnPath("poetry")!, ["install"], dir, [], "poetry install");
        if (File.Exists(Path.Combine(dir, "Pipfile")) && CliUtil.CommandExists("pipenv"))
            return new(CliUtil.FindOnPath("pipenv")!, ["install"], dir, [], "pipenv install");
        if (File.Exists(Path.Combine(dir, "requirements.txt")))
        {
            args.AddRange(["-m", "pip", "install", "-r", "requirements.txt"]);
            return new(py, args, dir, [], "pip install -r requirements.txt");
        }
        if (File.Exists(Path.Combine(dir, "pyproject.toml")))
        {
            args.AddRange(["-m", "pip", "install", "-e", "."]);
            return new(py, args, dir, [], "pip install -e .");
        }
        args.AddRange(["-m", "pip", "install", "-e", "."]);
        return new(py, args, dir, [], "pip install");
    }

    static ProcessPlan PlanPythonScript(string script)
    {
        var dir = Path.GetDirectoryName(script)!;
        var py = ToolchainBootstrap.ResolveCommand("python");
        var rel = Path.GetFileName(script);
        var args = new List<string>();
        if (py is not null && IsPyLauncher(py))
            args.Add("-3");
        args.Add(rel.Replace('\\', '/'));
        var env = new Dictionary<string, string> { ["PYTHONUNBUFFERED"] = "1" };
        if (py is null)
            return new("py", args, dir, ["python"], "python " + rel, env);
        return new(py, args, dir, [], "python " + rel, env);
    }

    static ProcessPlan PlanPythonStart(string dir)
    {
        foreach (var name in new[] { "manage.py", "app.py", "main.py", "asgi.py", "wsgi.py" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
            {
                if (name.Equals("manage.py", StringComparison.OrdinalIgnoreCase))
                {
                    var py = ToolchainBootstrap.ResolveCommand("python");
                    var args = new List<string>();
                    if (py is not null && IsPyLauncher(py))
                        args.Add("-3");
                    args.AddRange(["manage.py", "runserver"]);
                    if (py is null)
                        return new("py", args, dir, ["python"], "python manage.py runserver");
                    return new(py, args, dir, [], "python manage.py runserver");
                }
                return PlanPythonScript(path);
            }
        }
        return PlanPythonCompile(dir);
    }

    static ProcessPlan PlanMaven(string dir, IReadOnlyList<string> mavenArgs)
    {
        var wrapper = OperatingSystem.IsWindows()
            ? Path.Combine(dir, "mvnw.cmd")
            : Path.Combine(dir, "mvnw");
        if (File.Exists(wrapper))
            return new(wrapper, mavenArgs, dir, ToolchainBootstrap.IsInstalled("java") ? [] : ["java"], "mvn " + string.Join(' ', mavenArgs));
        var missing = new List<string>();
        if (!ToolchainBootstrap.IsInstalled("java"))
            missing.Add("java");
        if (!ToolchainBootstrap.IsInstalled("mvn"))
            missing.Add("mvn");
        var mvn = ToolchainBootstrap.ResolveCommand("mvn") ?? "mvn";
        return new(mvn, mavenArgs, dir, missing, "mvn " + string.Join(' ', mavenArgs));
    }

    static ProcessPlan PlanGradle(string dir, IReadOnlyList<string> gradleArgs)
    {
        var wrapper = OperatingSystem.IsWindows()
            ? Path.Combine(dir, "gradlew.bat")
            : Path.Combine(dir, "gradlew");
        if (File.Exists(wrapper))
            return new(wrapper, gradleArgs, dir, ToolchainBootstrap.IsInstalled("java") ? [] : ["java"], "./gradlew");
        var missing = new List<string>();
        if (!ToolchainBootstrap.IsInstalled("java"))
            missing.Add("java");
        var gradle = CliUtil.FindOnPath("gradle");
        if (gradle is null)
            missing.Add("java");
        return new(gradle ?? "gradle", gradleArgs, dir, missing, "gradle " + string.Join(' ', gradleArgs));
    }

    static ProcessPlan PlanPhpRestore(string dir)
    {
        var missing = new List<string>();
        if (!ToolchainBootstrap.IsInstalled("php"))
            missing.Add("php");
        var composer = ToolchainBootstrap.ResolveCommand("composer") ?? CliUtil.FindOnPath("composer");
        if (composer is null)
            missing.Add("composer");
        return new(composer ?? "composer", ["install"], dir, missing, "composer install");
    }

    static ProcessPlan PlanPhpStart(string dir)
    {
        var publicDir = Directory.Exists(Path.Combine(dir, "public")) ? "public" : ".";
        return Need("php", dir, ["-S", "127.0.0.1:8080", "-t", publicDir], "php -S");
    }

    static ProcessPlan PlanRubyRestore(string dir)
    {
        var bundle = CliUtil.FindOnPath("bundle");
        var missing = new List<string>();
        if (!ToolchainBootstrap.IsInstalled("ruby"))
            missing.Add("ruby");
        if (bundle is null)
            missing.Add("ruby");
        return new(bundle ?? "bundle", ["install"], dir, missing, "bundle install");
    }

    static ProcessPlan PlanRubyTest(string dir)
    {
        var bundle = CliUtil.FindOnPath("bundle");
        if (bundle is not null)
            return new(bundle, ["exec", "rake", "test"], dir, [], "bundle exec rake test");
        return Need("ruby", dir, ["-S", "rake", "test"], "rake test");
    }

    static ProcessPlan PlanRubyStart(string dir)
    {
        if (File.Exists(Path.Combine(dir, "bin", "rails")))
        {
            var bundle = CliUtil.FindOnPath("bundle");
            if (bundle is not null)
                return new(bundle, ["exec", "rails", "s"], dir, [], "rails s");
        }
        return Need("ruby", dir, [File.Exists(Path.Combine(dir, "config.ru")) ? "-S" : ""], "ruby");
    }

    static ProcessPlan PlanCMake(string dir)
    {
        var buildDir = Path.Combine(dir, "build");
        return Need("cmake", dir, ["--build", buildDir], "cmake --build");
    }

    static ProcessPlan PlanDartBuild(string dir, string? file)
    {
        if (file is not null)
        {
            try
            {
                if (TechStackCatalog.LooksLikeFlutter(File.ReadAllText(file)))
                    return NeedFlutterOrDart(dir, ["build", "web"], "flutter build");
            }
            catch (Exception) { /* ignore */ }
        }
        return Need("dart", dir, ["pub", "get"], "dart pub get");
    }

    static ProcessPlan PlanDartTest(string dir)
    {
        var flutter = CliUtil.FindOnPath("flutter");
        if (flutter is not null && File.Exists(Path.Combine(dir, "pubspec.yaml")))
            return new(flutter, ["test"], dir, [], "flutter test");
        return Need("dart", dir, ["test"], "dart test");
    }

    static ProcessPlan PlanDartRestore(string dir)
    {
        var flutter = CliUtil.FindOnPath("flutter");
        if (flutter is not null)
            return new(flutter, ["pub", "get"], dir, [], "flutter pub get");
        return Need("dart", dir, ["pub", "get"], "dart pub get");
    }

    static ProcessPlan PlanDartStart(string dir)
    {
        var flutter = CliUtil.FindOnPath("flutter");
        if (flutter is not null)
            return new(flutter, ["run"], dir, [], "flutter run");
        return Need("dart", dir, ["run"], "dart run");
    }

    static ProcessPlan NeedFlutterOrDart(string dir, IReadOnlyList<string> flutterArgs, string display)
    {
        var flutter = CliUtil.FindOnPath("flutter");
        if (flutter is not null)
            return new(flutter, flutterArgs, dir, [], display);
        return Need("dart", dir, ["pub", "get"], "dart pub get");
    }

    static bool IsPyLauncher(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Equals("py", StringComparison.OrdinalIgnoreCase);
}
