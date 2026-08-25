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
        Path.Combine(Path.GetFullPath(root), AppInfo.RuntimeDirName, "build-reports", Sanitize(target) + ".json");

    public static string Write(ProjectRuntime runtime, string target, int exitCode, string configuration)
    {
        runtime.Ensure();
        var id = Sanitize(target);
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
            return new BuildReport(
                JsonUtil.Str(obj["target"]),
                exit,
                status,
                utc,
                JsonUtil.Str(obj["configuration"]));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static string Sanitize(string target)
    {
        var name = Path.GetFileName(BuildFreshness.ToProjectDir("", target));
        if (string.IsNullOrEmpty(name))
            name = "build";
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray();
        var safe = new string(chars).Trim('-');
        return string.IsNullOrEmpty(safe) ? "build" : safe;
    }
}
