using System.Text.Json.Nodes;
using AiProject.Console.Core.Agents;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Runtime;

public static class ConsoleSettingsStore
{
    /// <summary>測試用：以 AsyncLocal 改寫 settings.json 路徑，避免平行測試互踩本機設定。</summary>
    static readonly AsyncLocal<string?> PathOverrideLocal = new();

    internal static string? PathOverride
    {
        get => PathOverrideLocal.Value;
        set => PathOverrideLocal.Value = value;
    }

    public static string SettingsPath()
    {
        if (!string.IsNullOrEmpty(PathOverride))
            return PathOverride;
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

    /// <summary>跨行程鎖定後讀改寫，避免兩個控制台互蓋歷史專案。</summary>
    public static void Mutate(Action<JsonObject> edit)
    {
        using var mutex = new Mutex(false, @"Local\sjvann.AIProjectConsole.settings");
        var taken = false;
        try
        {
            try { taken = mutex.WaitOne(); }
            catch (AbandonedMutexException) { taken = true; }
            var data = Load();
            edit(data);
            Save(data);
        }
        finally
        {
            if (taken)
            {
                try { mutex.ReleaseMutex(); }
                catch (ApplicationException) { }
            }
        }
    }

    public static void RememberProject(string root)
    {
        Mutate(data =>
        {
            var rootS = Path.GetFullPath(root);
            var recent = new JsonArray { rootS };
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
        });
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

    /// <summary>本視窗目前專案永遠列在歷史清單最前面，即使磁碟上的 recent 還沒寫入或被另一視窗蓋掉。</summary>
    public static IReadOnlyList<string> HistoryProjects(string? currentRoot, int limit = 12)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;
            try
            {
                if (!Directory.Exists(raw))
                    return;
                var key = Path.GetFullPath(raw);
                if (seen.Add(key))
                    list.Add(key);
            }
            catch (IOException)
            {
            }
        }

        Add(currentRoot);
        foreach (var p in RecentProjects(limit))
            Add(p);
        return list;
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
        Mutate(data => data.Remove("lastProject"));
    }

    /// <summary>
    /// 只清掉「目前這個視窗」關掉的專案，避免第二個視窗關閉時把第一個視窗的還原路徑抹掉。
    /// </summary>
    public static void ClearLastProjectIf(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;
        var want = Path.GetFullPath(root);
        Mutate(data =>
        {
            var last = JsonUtil.Str(data["lastProject"]);
            if (string.IsNullOrEmpty(last))
                return;
            if (!string.Equals(Path.GetFullPath(last), want, StringComparison.OrdinalIgnoreCase))
                return;
            data.Remove("lastProject");
        });
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
        Mutate(data => data["restoreLastProject"] = enabled);
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
        Mutate(data =>
        {
            data["openIdeOnLoad"] = enabled;
            data["openWithCursor"] = enabled;
        });
    }

    public static bool GetTestBeforePush() =>
        Load()["testBeforePush"]?.GetValue<bool>() ?? false;

    public static void SetTestBeforePush(bool enabled)
    {
        Mutate(data => data["testBeforePush"] = enabled);
    }

    public static string GetAgentProvider()
    {
        var raw = JsonUtil.Str(Load()["agentProvider"]);
        return string.IsNullOrWhiteSpace(raw) ? "cursor" : raw.Trim();
    }

    public static void SetAgentProvider(string id)
    {
        Mutate(data => data["agentProvider"] = string.IsNullOrWhiteSpace(id) ? "cursor" : id.Trim());
    }

    public static string GetAgentCliPath() => JsonUtil.Str(Load()["agentCliPath"]);

    public static void SetAgentCliPath(string? path) => SetOptionalString("agentCliPath", path);

    public static string GetCustomAgentCommand() => JsonUtil.Str(Load()["customAgentCommand"]);

    public static void SetCustomAgentCommand(string? command) => SetOptionalString("customAgentCommand", command);

    public static string GetCustomAgentArgs() => JsonUtil.Str(Load()["customAgentArgs"]);

    public static void SetCustomAgentArgs(string? args) => SetOptionalString("customAgentArgs", args);

    public static string GetTheme()
    {
        var raw = JsonUtil.Str(Load()["theme"]);
        return raw is "dark" or "light" ? raw : "light";
    }

    public static void SetTheme(string theme)
    {
        Mutate(data => data["theme"] = theme is "dark" ? "dark" : "light");
    }

    public static bool GetMcpReadOnly() =>
        Load()["mcpReadOnly"]?.GetValue<bool>() ?? false;

    public static void SetMcpReadOnly(bool enabled)
    {
        Mutate(data => data["mcpReadOnly"] = enabled);
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
        Mutate(data => data["mcpConfirm"] = (value ?? "").Trim());
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

    public static IReadOnlyList<ProjectAskSource> GetAskSources() =>
        ProjectAskProviders.Parse(Load()["askSources"]);

    public static void SetAskSources(IReadOnlyList<ProjectAskSource> sources)
    {
        Mutate(data =>
        {
            if (sources.Count == 0)
                data.Remove("askSources");
            else
                data["askSources"] = ProjectAskProviders.ToJson(sources);
        });
    }

    static void SetOptionalString(string key, string? value)
    {
        Mutate(data =>
        {
            var t = (value ?? "").Trim();
            if (string.IsNullOrEmpty(t))
                data.Remove(key);
            else
                data[key] = t;
        });
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
        Mutate(data => data["lastCloneParent"] = Path.GetFullPath(path));
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
        Mutate(data => data["updateLastCheckUtc"] = DateTimeOffset.UtcNow.ToString("o"));
    }

    public static string SkippedUpdateTag() => JsonUtil.Str(Load()["skippedUpdateTag"]);

    public static string GetGitHost()
    {
        var raw = JsonUtil.Str(Load()["gitHost"]);
        return string.IsNullOrWhiteSpace(raw) ? GitHost.PublicHostname : GitHost.Normalize(raw);
    }

    public static void SetGitHost(string? host)
    {
        Mutate(data =>
        {
            var value = GitHost.Normalize(host);
            data["gitHost"] = value;
            RememberGitHost(value, data);
        });
    }

    public static string GetGitKind()
    {
        var raw = JsonUtil.Str(Load()["gitKind"]);
        return string.IsNullOrWhiteSpace(raw) ? GitHost.KindGithub : GitHost.NormalizeKind(raw);
    }

    public static void SetGitKind(string? kind)
    {
        Mutate(data => data["gitKind"] = GitHost.NormalizeKind(kind));
    }

    public static IReadOnlyList<string> RecentGitHosts()
    {
        var data = Load();
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string host)
        {
            var n = GitHost.Normalize(host);
            if (seen.Add(n))
                list.Add(n);
        }
        Add(GetGitHost());
        Add(GitHost.PublicHostname);
        if (data["recentGitHosts"] is JsonArray arr)
        {
            foreach (var n in arr)
                Add(JsonUtil.Str(n));
        }
        return list;
    }

    public static void RememberGitHost(string host, JsonObject? data = null)
    {
        if (data is null)
        {
            Mutate(owned => RememberGitHost(host, owned));
            return;
        }
        var n = GitHost.Normalize(host);
        var recent = new JsonArray { n };
        if (data["recentGitHosts"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var s = JsonUtil.Str(item);
                if (!string.IsNullOrEmpty(s) && !string.Equals(s, n, StringComparison.OrdinalIgnoreCase))
                    recent.Add(s);
            }
        }
        while (recent.Count > 8)
            recent.RemoveAt(recent.Count - 1);
        data["recentGitHosts"] = recent;
    }

    public static string GetWorkbench()
    {
        var raw = JsonUtil.Str(Load()["workbench"]);
        return raw == "req" ? "req" : "dev";
    }

    public static void SetWorkbench(string? mode)
    {
        Mutate(data => data["workbench"] = mode == "req" ? "req" : "dev");
    }

    public static string GetCompanyBaseUrl() => JsonUtil.Str(Load()["companyBaseUrl"]);

    public static void SetCompanyBaseUrl(string? value) => SetOptionalString("companyBaseUrl", value);

    public static IReadOnlyList<ReportingDestination> GetReportingDestinations()
    {
        IReadOnlyList<ReportingDestination>? result = null;
        Mutate(data =>
        {
            result = ReportingDestinationsStore.LoadAndMigrate(data);
        });
        return result ?? [];
    }

    public static string? GetSelectedReportingDestinationId() =>
        ReportingDestinationsStore.SelectedId(Load());

    public static void SetReportingDestinations(IReadOnlyList<ReportingDestination> destinations, string? selectedId)
    {
        Mutate(data => ReportingDestinationsStore.Save(data, destinations, selectedId));
    }

    public static IReadOnlyDictionary<string, Guid> GetCompanyProjectMap()
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (Load()["companyProjectMap"] is not JsonObject obj)
            return map;
        foreach (var (key, node) in obj)
        {
            var raw = JsonUtil.Str(node);
            if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
                map[key] = id;
        }
        return map;
    }

    public static void SetCompanyProjectId(string projectKey, Guid projectId)
    {
        Mutate(data =>
        {
            var obj = data["companyProjectMap"] as JsonObject ?? [];
            var key = (projectKey ?? "").Trim();
            if (string.IsNullOrEmpty(key) || projectId == Guid.Empty)
                obj.Remove(key);
            else
                obj[key] = projectId.ToString();
            data["companyProjectMap"] = obj;
        });
    }

    public static void SetSkippedUpdateTag(string? tag)
    {
        Mutate(data =>
        {
            var t = (tag ?? "").Trim();
            if (string.IsNullOrEmpty(t))
                data.Remove("skippedUpdateTag");
            else
                data["skippedUpdateTag"] = t;
        });
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
