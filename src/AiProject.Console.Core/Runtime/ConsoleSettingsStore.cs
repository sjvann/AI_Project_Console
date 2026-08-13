using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Runtime;

public static class ConsoleSettingsStore
{
    public static string SettingsPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            return Path.Combine(baseDir, AppInfo.SettingsDirName, "settings.json");
        }
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var unix = string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdg;
        return Path.Combine(unix, AppInfo.SettingsDirName, "settings.json");
    }

    public static JsonObject Load() => JsonUtil.LoadObject(SettingsPath());

    public static void Save(JsonObject data) => JsonUtil.SaveObject(SettingsPath(), data);

    public static void RememberProject(string root)
    {
        var data = Load();
        var rootS = Path.GetFullPath(root);
        var recent = new JsonArray();
        recent.Add(rootS);
        if (data["recentProjects"] is JsonArray arr)
        {
            foreach (var n in arr)
            {
                var s = JsonUtil.Str(n);
                if (!string.IsNullOrEmpty(s) && !string.Equals(s, rootS, StringComparison.OrdinalIgnoreCase))
                    recent.Add(s);
            }
        }
        while (recent.Count > 12)
            recent.RemoveAt(recent.Count - 1);
        data["recentProjects"] = recent;
        data["lastProject"] = rootS;
        Save(data);
    }

    public static IReadOnlyList<string> RecentProjects(int limit = 12)
    {
        var data = Load();
        var outList = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (data["recentProjects"] is not JsonArray arr)
            return outList;
        foreach (var n in arr)
        {
            var raw = JsonUtil.Str(n);
            if (string.IsNullOrEmpty(raw) || !Directory.Exists(raw))
                continue;
            var key = Path.GetFullPath(raw);
            if (!seen.Add(key))
                continue;
            outList.Add(key);
            if (outList.Count >= limit)
                break;
        }
        return outList;
    }

    public static bool GetOpenWithCursor()
    {
        var data = Load();
        if (data["openWithCursor"] is null)
            return true;
        return data["openWithCursor"]?.GetValue<bool>() ?? true;
    }

    public static void SetOpenWithCursor(bool enabled)
    {
        var data = Load();
        data["openWithCursor"] = enabled;
        Save(data);
    }

    public static string? LastCloneParent()
    {
        var raw = JsonUtil.Str(Load()["lastCloneParent"]);
        if (string.IsNullOrEmpty(raw) || !Directory.Exists(raw))
            return null;
        return Path.GetFullPath(raw);
    }

    public static void SetLastCloneParent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        var data = Load();
        data["lastCloneParent"] = Path.GetFullPath(path);
        Save(data);
    }

    public static bool ShouldAutoCheckUpdate(TimeSpan minInterval)
    {
        var raw = JsonUtil.Str(Load()["updateLastCheckUtc"]);
        if (!DateTimeOffset.TryParse(raw, out var last))
            return true;
        return DateTimeOffset.UtcNow - last >= minInterval;
    }

    public static void MarkUpdateChecked()
    {
        var data = Load();
        data["updateLastCheckUtc"] = DateTimeOffset.UtcNow.ToString("o");
        Save(data);
    }

    public static string SkippedUpdateTag() => JsonUtil.Str(Load()["skippedUpdateTag"]);

    public static void SetSkippedUpdateTag(string? tag)
    {
        var data = Load();
        var t = (tag ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            data.Remove("skippedUpdateTag");
        else
            data["skippedUpdateTag"] = t;
        Save(data);
    }
}

public sealed class ProjectRuntime
{
    public string Root { get; }
    public string Base { get; }
    public string Logs { get; }
    public string Pids { get; }
    public string BuildReports { get; }
    public string UatReports { get; }

    public ProjectRuntime(string root)
    {
        Root = Path.GetFullPath(root);
        Base = Path.Combine(Root, AppInfo.RuntimeDirName);
        Logs = Path.Combine(Base, "logs");
        Pids = Path.Combine(Base, "pids");
        BuildReports = Path.Combine(Base, "build-reports");
        UatReports = Path.Combine(Base, "uat-reports");
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Pids);
        Directory.CreateDirectory(BuildReports);
        Directory.CreateDirectory(UatReports);
    }

    public string LogPath(string stem)
    {
        Ensure();
        return Path.Combine(Logs, stem + ".log");
    }

    public string PidPath(string stem)
    {
        Ensure();
        return Path.Combine(Pids, stem + ".pid");
    }
}
