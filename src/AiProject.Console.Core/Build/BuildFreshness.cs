using System.Diagnostics;
using System.Xml.Linq;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Build;

public static class BuildFreshness
{
    public const string DefaultConfiguration = "Debug";

    private static readonly HashSet<string> SourceSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".razor", ".cshtml", ".csproj", ".props", ".targets",
        ".js", ".css", ".html", ".proto", ".resx",
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

    public static (double Mtime, string? Path) BuildOutput(string projectDir, string configuration = DefaultConfiguration)
    {
        var name = AssemblyName(projectDir);
        var binDir = Path.Combine(projectDir, "bin");
        if (!Directory.Exists(binDir))
            return (0, null);

        var preferred = NewestDll(binDir, name, configuration);
        if (preferred.Path is not null)
            return preferred;
        return NewestDll(binDir, name, configuration: null);
    }

    static (double Mtime, string? Path) NewestDll(string binDir, string assemblyName, string? configuration)
    {
        double best = 0;
        string? bestPath = null;
        foreach (var dll in Directory.EnumerateFiles(binDir, assemblyName + ".dll", SearchOption.AllDirectories))
        {
            if (configuration is not null && !PathContainsConfig(dll, configuration))
                continue;
            var mtime = File.GetLastWriteTimeUtc(dll).Subtract(DateTime.UnixEpoch).TotalSeconds;
            if (mtime > best)
            {
                best = mtime;
                bestPath = dll;
            }
        }
        return (best, bestPath);
    }

    static bool PathContainsConfig(string dllPath, string configuration)
    {
        var parts = dllPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var i = Array.FindIndex(parts, p => p.Equals("bin", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < parts.Length
            && parts[i + 1].Equals(configuration, StringComparison.OrdinalIgnoreCase);
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

    public static bool SameProject(string root, string relOrPath, string target)
    {
        var a = ToProjectDir(root, relOrPath);
        var b = ToProjectDir(root, target);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static string ToProjectDir(string root, string relOrPath)
    {
        var raw = (relOrPath ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return "";
        var full = Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(root, raw.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(full) || Path.GetExtension(full).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(full) ?? full;
        return full;
    }

    public static BuildState ServiceBuildState(ProjectCatalog catalog, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        var projectDir = ProjectDirForService(catalog, host);
        return FromScan(catalog.Root, projectDir, new BuildState(
            Id: svc.Id,
            Name: svc.Label,
            Status: "",
            Path: host.Project,
            Output: "",
            Label: svc.Label));
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
        var kind = info.IsTest ? "測試" : (info.IsExecutable || info.IsWeb ? "核心" : "函式庫");
        return FromScan(root, projectDir, new BuildState(
            Id: info.RelDir,
            Name: info.Name,
            Status: "",
            Path: info.RelDir,
            System: info.Group,
            Kind: kind,
            Output: "",
            Language: info.Language));
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

    public static string FormatAgo(DateTimeOffset? utc, DateTimeOffset? now = null)
    {
        if (utc is null)
            return "";
        var n = now ?? DateTimeOffset.UtcNow;
        var delta = n - utc.Value;
        if (delta < TimeSpan.Zero)
            delta = TimeSpan.Zero;
        if (delta.TotalSeconds < 45)
            return "剛剛";
        if (delta.TotalMinutes < 60)
            return $"{Math.Max(1, (int)delta.TotalMinutes)} 分鐘前";
        if (delta.TotalHours < 24)
            return $"{Math.Max(1, (int)delta.TotalHours)} 小時前";
        if (delta.TotalDays < 14)
            return $"{Math.Max(1, (int)delta.TotalDays)} 天前";
        return utc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    public static string BadgeText(BuildState state) =>
        state.Activity switch
        {
            "queued" => "等待",
            "building" => "編譯中",
            "ok" => "完成",
            "failed" => "失敗",
            _ => state.Status switch
            {
                "stale" => "需重編",
                "unbuilt" => "未建置",
                "fresh" => "最新",
                _ => "",
            },
        };

    public static string RowKind(BuildState state) =>
        state.Activity switch
        {
            "queued" => "queued",
            "building" => "building",
            "ok" => "ok",
            "failed" => "failed",
            _ => state.Status switch
            {
                "stale" => "stale",
                "unbuilt" => "unbuilt",
                "fresh" => "fresh",
                _ => "",
            },
        };

    static BuildState FromScan(string root, string projectDir, BuildState seed)
    {
        var (srcMtime, srcPath) = NewestSource(projectDir);
        var (outMtime, outPath) = BuildOutput(projectDir);
        var status = outMtime <= 0 ? "unbuilt" : srcMtime > outMtime ? "stale" : "fresh";
        DateTimeOffset? lastBuild = outMtime > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)outMtime)
            : null;
        DateTimeOffset? newestSrc = srcMtime > 0
            ? DateTimeOffset.FromUnixTimeSeconds((long)srcMtime)
            : null;
        var reason = Describe(status, srcPath, srcMtime, outPath, outMtime);
        var report = BuildReportStore.TryRead(root, projectDir);
        if (report is not null)
        {
            if (lastBuild is null || report.CompletedUtc > lastBuild)
                lastBuild = report.CompletedUtc;
            if (report.Ok && newestSrc is { } src && report.CompletedUtc >= src)
            {
                status = "fresh";
                reason = "控制台編譯成功，且之後沒有更新的來源檔";
            }
            else if (!report.Ok)
                reason += "；控制台上次編譯失敗（" + FormatAgo(report.CompletedUtc) + "）";
        }

        // mtime 會被 checkout／掃描器整批改寫；Git 乾淨且最後提交早於 DLL 時不要誤報需重編。
        if (status == "stale" && outMtime > 0 && (report is null || report.Ok)
            && TryGitCleanContentStamp(root, projectDir, out var gitUnix) && gitUnix <= outMtime)
        {
            status = "fresh";
            reason = "來源內容未變（Git 乾淨），僅檔案時間較新，無需重編";
        }

        return seed with
        {
            Status = status,
            Output = outPath ?? "",
            LastBuildUtc = lastBuild,
            NewestSourceUtc = newestSrc,
            NewestSourcePath = srcPath ?? "",
            Reason = reason,
        };
    }

    static string Describe(string status, string? srcPath, double srcMtime, string? outPath, double outMtime)
    {
        if (status == "unbuilt")
            return "尚未找到編譯輸出（bin/" + DefaultConfiguration + "）";
        var srcName = string.IsNullOrEmpty(srcPath) ? "來源" : Path.GetFileName(srcPath);
        var dllName = string.IsNullOrEmpty(outPath) ? "輸出 DLL" : Path.GetFileName(outPath);
        if (status == "stale")
        {
            var delta = TimeSpan.FromSeconds(Math.Max(0, srcMtime - outMtime));
            return $"{srcName} 比 {dllName} 新 {FormatDuration(delta)}，需重編";
        }
        return $"已與 {dllName} 一致，無需重編";
    }

    static string FormatDuration(TimeSpan delta)
    {
        if (delta.TotalSeconds < 45)
            return "不到 1 分鐘";
        if (delta.TotalMinutes < 60)
            return $"{Math.Max(1, (int)delta.TotalMinutes)} 分鐘";
        if (delta.TotalHours < 24)
            return $"{Math.Max(1, (int)delta.TotalHours)} 小時";
        return $"{Math.Max(1, (int)delta.TotalDays)} 天";
    }

    /// <summary>
    /// Last commit time (unix seconds) of <paramref name="projectDir"/> when that tree has no
    /// dirty compile sources. False if not a git repo, git missing, or sources are dirty.
    /// </summary>
    internal static bool TryGitCleanContentStamp(string root, string projectDir, out double commitUnix)
    {
        commitUnix = 0;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(projectDir))
            return false;
        string rel;
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var fullDir = Path.GetFullPath(projectDir);
            if (!fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                return false;
            rel = Path.GetRelativePath(fullRoot, fullDir);
            if (string.IsNullOrWhiteSpace(rel) || rel == ".")
                rel = ".";
        }
        catch
        {
            return false;
        }

        var git = CliUtil.FindOnPath("git");
        if (string.IsNullOrEmpty(git))
            return false;
        if (RunGit(git, root, ["rev-parse", "--is-inside-work-tree"]) is not ("true", 0))
            return false;
        var (toplevel, topCode) = RunGit(git, root, ["rev-parse", "--show-toplevel"]);
        if (topCode != 0 || string.IsNullOrWhiteSpace(toplevel))
            return false;
        if (!string.Equals(Path.GetFullPath(toplevel), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            return false;

        var (porcelain, stCode) = RunGit(git, root, ["-c", "core.quotepath=false", "status", "--porcelain", "--", rel]);
        if (stCode != 0)
            return false;
        if (HasDirtyCompileSource(porcelain))
            return false;

        var (log, logCode) = RunGit(git, root, ["log", "-1", "--format=%ct", "--", rel]);
        if (logCode != 0 || string.IsNullOrWhiteSpace(log))
            return false;
        return double.TryParse(log.Trim(), out commitUnix) && commitUnix > 0;
    }

    static bool HasDirtyCompileSource(string porcelain)
    {
        if (string.IsNullOrWhiteSpace(porcelain))
            return false;
        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length < 4)
                continue;
            var path = line[3..].Trim().Replace('/', Path.DirectorySeparatorChar);
            if (path.Contains(" -> ", StringComparison.Ordinal))
                path = path.Split(" -> ", 2)[^1];
            if (IsRuntimeState(path))
                continue;
            if (SourceSuffixes.Contains(Path.GetExtension(path)))
                return true;
        }
        return false;
    }

    static (string Output, int Code) RunGit(string git, string root, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = git,
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null)
                return ("", 1);
            if (!proc.WaitForExit(8_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return ("", 1);
            }
            var stdout = proc.StandardOutput.ReadToEnd().Trim();
            return (stdout, proc.ExitCode);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return ("", 1);
        }
    }
}
