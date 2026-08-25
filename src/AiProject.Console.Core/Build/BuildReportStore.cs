using System.Text.Json;
using AiProject.Console.Core.Runtime;

namespace AiProject.Console.Core.Build;

public static class BuildReportStore
{
    static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

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
