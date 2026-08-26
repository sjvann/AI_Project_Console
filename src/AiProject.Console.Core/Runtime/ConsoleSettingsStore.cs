using System.Text.Json.Nodes;
using AiProject.Console.Core.Agents;
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

    public static string? LastProject()
    {
        var raw = JsonUtil.Str(Load()["lastProject"]);
        if (string.IsNullOrEmpty(raw) || !Directory.Exists(raw))
            return null;
        return Path.GetFullPath(raw);
    }

    public static void ClearLastProject()
    {
        var data = Load();
        data.Remove("lastProject");
        Save(data);
    }

    public static bool GetRestoreLastProject()
    {
        var data = Load();
        if (data["restoreLastProject"] is null)
            return true;
        return data["restoreLastProject"]?.GetValue<bool>() ?? true;
    }

    public static void SetRestoreLastProject(bool enabled)
    {
        var data = Load();
        data["restoreLastProject"] = enabled;
        Save(data);
    }

    public static bool GetOpenWithCursor() => GetOpenIdeOnLoad();

    public static void SetOpenWithCursor(bool enabled) => SetOpenIdeOnLoad(enabled);

    public static bool GetOpenIdeOnLoad()
    {
        var data = Load();
        if (data["openIdeOnLoad"] is not null)
            return data["openIdeOnLoad"]?.GetValue<bool>() ?? true;
        if (data["openWithCursor"] is null)
            return true;
        return data["openWithCursor"]?.GetValue<bool>() ?? true;
    }

    public static void SetOpenIdeOnLoad(bool enabled)
    {
        var data = Load();
        data["openIdeOnLoad"] = enabled;
        data["openWithCursor"] = enabled;
        Save(data);
    }

    public static bool GetTestBeforePush() =>
        Load()["testBeforePush"]?.GetValue<bool>() ?? false;

    public static void SetTestBeforePush(bool enabled)
    {
        var data = Load();
        data["testBeforePush"] = enabled;
        Save(data);
    }

    public static string GetAgentProvider()
    {
        var raw = JsonUtil.Str(Load()["agentProvider"]);
        return string.IsNullOrWhiteSpace(raw) ? "cursor" : raw.Trim();
    }

    public static void SetAgentProvider(string id)
    {
        var data = Load();
        data["agentProvider"] = string.IsNullOrWhiteSpace(id) ? "cursor" : id.Trim();
        Save(data);
    }

    public static string GetAgentCliPath() => JsonUtil.Str(Load()["agentCliPath"]);

    public static void SetAgentCliPath(string? path)
    {
        var data = Load();
        var t = (path ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            data.Remove("agentCliPath");
        else
            data["agentCliPath"] = t;
        Save(data);
    }

    public static string GetCustomAgentCommand() => JsonUtil.Str(Load()["customAgentCommand"]);

    public static void SetCustomAgentCommand(string? command)
    {
        var data = Load();
        var t = (command ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            data.Remove("customAgentCommand");
        else
            data["customAgentCommand"] = t;
        Save(data);
    }

    public static string GetCustomAgentArgs() => JsonUtil.Str(Load()["customAgentArgs"]);

    public static void SetCustomAgentArgs(string? args)
    {
        var data = Load();
        var t = (args ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            data.Remove("customAgentArgs");
        else
            data["customAgentArgs"] = t;
        Save(data);
    }

    public static string GetTheme()
    {
        var raw = JsonUtil.Str(Load()["theme"]);
        return raw is "dark" or "light" ? raw : "light";
    }

    public static void SetTheme(string theme)
    {
        var data = Load();
        data["theme"] = theme is "dark" ? "dark" : "light";
        Save(data);
    }

    public static bool GetMcpReadOnly() =>
        Load()["mcpReadOnly"]?.GetValue<bool>() ?? false;

    public static void SetMcpReadOnly(bool enabled)
    {
        var data = Load();
        data["mcpReadOnly"] = enabled;
        Save(data);
    }

    public static string GetMcpAllow() => JsonUtil.Str(Load()["mcpAllow"]);

    public static void SetMcpAllow(string? value) => SetOptionalString("mcpAllow", value);

    public static string GetMcpDeny() => JsonUtil.Str(Load()["mcpDeny"]);

    public static void SetMcpDeny(string? value) => SetOptionalString("mcpDeny", value);

    /// <summary>未寫入時預設 stop_all；明確存空白＝不要求確認。</summary>
    public static string GetMcpConfirm()
    {
        var data = Load();
        if (data["mcpConfirm"] is null)
            return "stop_all";
        return JsonUtil.Str(data["mcpConfirm"]);
    }

    public static void SetMcpConfirm(string? value)
    {
        var data = Load();
        data["mcpConfirm"] = (value ?? "").Trim();
        Save(data);
    }

    public static string GetAskBaseUrl()
    {
        var raw = JsonUtil.Str(Load()["askBaseUrl"]);
        return string.IsNullOrWhiteSpace(raw) ? ProjectAskService.DefaultBaseUrl : raw.Trim();
    }

    public static void SetAskBaseUrl(string? value) => SetOptionalString("askBaseUrl", value);

    public static string GetAskApiKey() => JsonUtil.Str(Load()["askApiKey"]);

    public static void SetAskApiKey(string? value) => SetOptionalString("askApiKey", value);

    public static string GetAskModel()
    {
        var raw = JsonUtil.Str(Load()["askModel"]);
        return string.IsNullOrWhiteSpace(raw) ? ProjectAskService.DefaultModel : raw.Trim();
    }

    public static void SetAskModel(string? value) => SetOptionalString("askModel", value);

    static void SetOptionalString(string key, string? value)
    {
        var data = Load();
        var t = (value ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            data.Remove(key);
        else
            data[key] = t;
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
