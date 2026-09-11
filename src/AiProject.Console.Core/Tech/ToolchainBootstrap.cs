using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tech;

public static class ToolchainBootstrap
{
    public static bool HasWinget() => CliUtil.CommandExists("winget");

    public static string? ResolveCommand(string toolId)
    {
        var spec = TechStackCatalog.Tool(toolId);
        if (spec is null)
            return CliUtil.FindOnPath(toolId);
        foreach (var cmd in spec.Commands)
        {
            var found = CliUtil.FindOnPath(cmd);
            if (found is not null)
                return found;
        }
        return null;
    }

    public static bool IsInstalled(string toolId)
    {
        if (toolId.Equals("python", StringComparison.OrdinalIgnoreCase))
            return ResolveCommand("python") is not null;
        if (toolId.Equals("java", StringComparison.OrdinalIgnoreCase))
            return CliUtil.CommandExists("java") || CliUtil.CommandExists("javac");
        if (toolId.Equals("mvn", StringComparison.OrdinalIgnoreCase))
            return CliUtil.CommandExists("mvn");
        if (toolId.Equals("dart", StringComparison.OrdinalIgnoreCase))
            return CliUtil.CommandExists("dart") || CliUtil.CommandExists("flutter");
        return ResolveCommand(toolId) is not null;
    }

    public static bool WrapperExists(string projectDir, string toolId)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return false;
        if (toolId.Equals("mvn", StringComparison.OrdinalIgnoreCase))
            return File.Exists(Path.Combine(projectDir, "mvnw"))
                || File.Exists(Path.Combine(projectDir, "mvnw.cmd"));
        if (toolId.Equals("java", StringComparison.OrdinalIgnoreCase))
            return File.Exists(Path.Combine(projectDir, "gradlew"))
                || File.Exists(Path.Combine(projectDir, "gradlew.bat"));
        return false;
    }

    public static bool IsSatisfied(string toolId, string? projectDir = null)
    {
        if (IsInstalled(toolId))
            return true;
        if (string.IsNullOrEmpty(projectDir))
            return false;
        if (toolId.Equals("mvn", StringComparison.OrdinalIgnoreCase))
            return WrapperExists(projectDir, "mvn");
        if (toolId.Equals("java", StringComparison.OrdinalIgnoreCase)
            && (File.Exists(Path.Combine(projectDir, "gradlew"))
                || File.Exists(Path.Combine(projectDir, "gradlew.bat"))))
            return IsInstalled("java");
        return false;
    }

    public static IReadOnlyList<ToolStatus> Inspect(IEnumerable<string> toolIds)
    {
        var list = new List<ToolStatus>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in toolIds)
        {
            if (!seen.Add(id))
                continue;
            var spec = TechStackCatalog.Tool(id);
            if (spec is null)
                continue;
            var path = ResolveCommand(id);
            list.Add(new ToolStatus(spec, path is not null || IsInstalled(id), path));
        }
        return list;
    }

    public static IReadOnlyList<ToolSpec> MissingTools(IEnumerable<string> toolIds, string? projectDir = null)
    {
        var missing = new List<ToolSpec>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in toolIds)
        {
            if (!seen.Add(id))
                continue;
            var spec = TechStackCatalog.Tool(id);
            if (spec is null)
                continue;
            if (!IsSatisfied(id, projectDir))
                missing.Add(spec);
        }
        return missing;
    }

    public static string FormatMissing(IEnumerable<string> toolIds)
    {
        var specs = toolIds
            .Select(TechStackCatalog.Tool)
            .Where(s => s is not null)
            .Select(s => s!)
            .DistinctBy(s => s.Id)
            .ToList();
        if (specs.Count == 0)
            return "缺少開發環境。";
        var names = string.Join("、", specs.Select(s => s.DisplayName));
        var how = string.Join("\n", specs.Select(s => s.HowTo));
        return $"缺少開發環境：{names}。請到「環境體檢」一鍵安裝，或：\n{how}";
    }

    public static void RefreshProcessPath()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            var machine = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
            var user = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "";
            var current = Environment.GetEnvironmentVariable("PATH") ?? "";
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in (user + Path.PathSeparator + machine + Path.PathSeparator + current)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = part.Trim();
                if (p.Length == 0 || !seen.Add(p))
                    continue;
                merged.Add(p);
            }
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, merged));
        }
        catch (Exception)
        {
            // ignore
        }
    }

    public static async Task<(int Code, string Output)> InstallAsync(
        string toolId,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var spec = TechStackCatalog.Tool(toolId);
        if (spec is null)
            return (1, "未知工具：" + toolId);

        void Emit(string line) => progress?.Report(line);

        if (IsInstalled(toolId))
        {
            Emit(spec.DisplayName + " 已安裝。");
            return (0, spec.DisplayName + " 已安裝。");
        }

        if (!OperatingSystem.IsWindows() || !HasWinget())
        {
            try { CliUtil.OpenUrl(spec.DownloadUrl); }
            catch (Exception ex) { Emit(ex.Message); }
            var msg = "無法用 winget 自動安裝。已開啟下載頁：\n" + spec.HowTo;
            Emit(msg);
            return (1, msg);
        }

        Emit("winget install " + spec.WingetId + " …");
        var args = new[]
        {
            "install", "-e", "--id", spec.WingetId,
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity",
        };
        var (code, output) = await CliUtil.RunAsync(
            "winget", args, timeoutMs: 600_000, ct: ct, onLine: line => Emit(line ?? "")).ConfigureAwait(false);
        RefreshProcessPath();
        if (code == 0 || IsInstalled(toolId))
        {
            Emit("安裝完成：" + spec.DisplayName);
            return (0, string.IsNullOrWhiteSpace(output) ? "已安裝 " + spec.DisplayName : output);
        }
        try { CliUtil.OpenUrl(spec.DownloadUrl); }
        catch (Exception) { /* ignore */ }
        var fail = "winget 安裝失敗。已開啟官方下載頁。\n" + spec.HowTo + "\n" + output;
        Emit(fail);
        return (1, fail);
    }
}
