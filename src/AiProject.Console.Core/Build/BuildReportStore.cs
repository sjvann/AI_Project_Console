using System.Text.Json;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Build;

public sealed record BuildReport(
    string Target,
    int ExitCode,
    string Status,
    DateTimeOffset CompletedUtc,
    string Configuration)
{
    public bool Ok => ExitCode == 0 || Status == "ok";
}

public static class BuildReportStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string FilePath(string root, string target) =>
        Path.Combine(Path.GetFullPath(root), AppInfo.RuntimeDirName, "build-reports", Sanitize(target, root) + ".json");

    public static string Write(ProjectRuntime runtime, string target, int exitCode, string configuration)
    {
        runtime.Ensure();
        var id = Sanitize(target, runtime.Root);
        var path = Path.Combine(runtime.BuildReports, id + ".json");
        var report = new Dictionary<string, object?>
        {
            ["target"] = target,
            ["exitCode"] = exitCode,
            ["configuration"] = configuration,
            ["status"] = exitCode == 0 ? "ok" : "failed",
            ["completedUtc"] = DateTimeOffset.UtcNow.ToString("o"),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOpts));
        return path;
    }

    public static BuildReport? TryRead(string root, string target)
    {
        var path = FilePath(root, target);
        if (!File.Exists(path))
            return null;
        try
        {
            var obj = JsonUtil.LoadObject(path);
            if (!DateTimeOffset.TryParse(JsonUtil.Str(obj["completedUtc"]), out var utc))
                return null;
            var exit = 1;
            try { exit = obj["exitCode"]?.GetValue<int>() ?? 1; }
            catch (InvalidOperationException)
            {
                _ = int.TryParse(JsonUtil.Str(obj["exitCode"]), out exit);
            }

            var status = JsonUtil.Str(obj["status"]);
            if (string.IsNullOrEmpty(status))
                status = exit == 0 ? "ok" : "failed";
            var report = new BuildReport(
                JsonUtil.Str(obj["target"]),
                exit,
                status,
                utc,
                JsonUtil.Str(obj["configuration"]));
            // 舊版只留資料夾名（Client.json），所有模組 Client 會搶同一份；對不上目前路徑就當沒有。
            if (!ReportBelongsTo(report.Target, root, target))
                return null;
            return report;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string Sanitize(string target) => Sanitize(target, root: null);

    public static string Sanitize(string target, string? root)
    {
        var dir = BuildFreshness.ToProjectDir(root ?? "", target);
        if (!string.IsNullOrWhiteSpace(root))
        {
            try
            {
                var fullRoot = Path.GetFullPath(root);
                var fullDir = Path.GetFullPath(dir);
                if (fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    var rel = Path.GetRelativePath(fullRoot, fullDir);
                    if (!string.IsNullOrWhiteSpace(rel) && rel != ".")
                        return SafeFileId(rel);
                }
            }
            catch
            {
                /* fall through */
            }
        }

        return SafeFileId(LastSegments(dir, 4));
    }

    static bool ReportBelongsTo(string storedTarget, string root, string target)
    {
        if (string.IsNullOrWhiteSpace(storedTarget))
            return true;
        var stored = BuildFreshness.ToProjectDir(root, storedTarget);
        var current = BuildFreshness.ToProjectDir(root, target);
        return string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
    }

    static string LastSegments(string path, int n)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => p.Length > 0 && p is not "." && !p.EndsWith(':'))
            .ToArray();
        if (parts.Length == 0)
            return "build";
        return string.Join("-", parts.Skip(Math.Max(0, parts.Length - n)));
    }

    static string SafeFileId(string rel)
    {
        var chars = rel.Select(c =>
            c is '/' or '\\' ? '-' :
            Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray();
        var safe = new string(chars).Trim('-');
        if (safe.Length > 120)
            safe = safe[^120..].Trim('-');
        return string.IsNullOrEmpty(safe) ? "build" : safe;
    }
}
