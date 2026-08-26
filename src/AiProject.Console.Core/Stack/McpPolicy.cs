using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Stack;

public sealed record McpDecision(bool Allowed, bool NeedConfirm, string? Error)
{
    public static McpDecision Ok() => new(true, false, null);

    public static McpDecision Deny(string error) => new(false, false, error);

    public static McpDecision Confirm(string error) => new(false, true, error);
}

/// <summary>
/// Agent 可呼叫哪些堆疊工具。使用者設定為底，專案 <c>.ai_project/mcp-policy.json</c> 覆蓋同名欄位。
/// </summary>
public sealed record McpPolicy
{
    public static readonly IReadOnlyList<string> ReadOnlyTools =
    [
        "stack_status", "list_services", "list_projects", "build_freshness",
        "get_log", "doctor", "git_status", "list_audit", "duty_summary",
        "docs_status", "list_docs", "read_doc",
    ];

    public static readonly IReadOnlyList<string> DefaultConfirmTools = ["stop_all"];

    public bool ReadOnly { get; init; }
    public IReadOnlyList<string> Allow { get; init; } = [];
    public IReadOnlyList<string> Deny { get; init; } = [];
    public IReadOnlyList<string> Confirm { get; init; } = DefaultConfirmTools;
    public string Source { get; init; } = "defaults";

    public static McpPolicy Defaults() => new()
    {
        ReadOnly = false,
        Allow = [],
        Deny = [],
        Confirm = DefaultConfirmTools,
        Source = "defaults",
    };

    public static string FilePath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot), AppInfo.RuntimeDirName, "mcp-policy.json");

    public static McpPolicy FromUserSettings()
    {
        var data = ConsoleSettingsStore.Load();
        return new McpPolicy
        {
            ReadOnly = ConsoleSettingsStore.GetMcpReadOnly(),
            Allow = SplitNames(ConsoleSettingsStore.GetMcpAllow()),
            Deny = SplitNames(ConsoleSettingsStore.GetMcpDeny()),
            Confirm = data["mcpConfirm"] is null
                ? DefaultConfirmTools
                : SplitNames(ConsoleSettingsStore.GetMcpConfirm()),
            Source = "settings",
        };
    }

    public static McpPolicy Overlay(McpPolicy fallback, JsonObject obj)
    {
        return new McpPolicy
        {
            ReadOnly = obj["readOnly"] is null ? fallback.ReadOnly : Truthy(obj["readOnly"]),
            Allow = obj["allow"] is null ? fallback.Allow : ParseNames(obj["allow"]),
            Deny = obj["deny"] is null ? fallback.Deny : ParseNames(obj["deny"]),
            Confirm = obj["confirm"] is null ? fallback.Confirm : ParseNames(obj["confirm"]),
            Source = "project",
        };
    }

    /// <summary>
    /// 載入政策。測試可傳 <paramref name="user"/>＝<see cref="Defaults"/>，避免讀到本機 settings。
    /// </summary>
    public static McpPolicy Load(string? projectRoot, McpPolicy? user = null)
    {
        var policy = user ?? FromUserSettings();
        if (string.IsNullOrWhiteSpace(projectRoot))
            return policy;
        var path = FilePath(projectRoot);
        if (!File.Exists(path))
            return policy;
        var obj = JsonUtil.LoadObject(path);
        return Overlay(policy, obj);
    }

    public static string WriteTemplate(string projectRoot)
    {
        var path = FilePath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var obj = new JsonObject
        {
            ["readOnly"] = false,
            ["allow"] = new JsonArray(),
            ["deny"] = new JsonArray(),
            ["confirm"] = new JsonArray { "stop_all" },
        };
        File.WriteAllText(path, obj.ToJsonString(JsonUtil.Options) + "\n");
        return path;
    }

    public bool IsAllowed(string tool)
    {
        var key = Norm(tool);
        if (string.IsNullOrEmpty(key))
            return false;
        if (Contains(Deny, key))
            return false;
        if (Allow.Count > 0 && !Contains(Allow, key))
            return false;
        if (ReadOnly && !Contains(ReadOnlyTools, key))
            return false;
        return true;
    }

    public bool RequiresConfirm(string tool) => Contains(Confirm, Norm(tool));

    public McpDecision Decide(string tool, IReadOnlyDictionary<string, string?>? args = null)
    {
        var key = Norm(tool);
        if (!IsAllowed(key))
        {
            if (Contains(Deny, key))
                return McpDecision.Deny("政策禁止工具：" + key);
            if (ReadOnly && !Contains(ReadOnlyTools, key))
                return McpDecision.Deny("MCP 唯讀模式，禁止：" + key + "。請在設定或 mcp-policy.json 關閉 readOnly。");
            return McpDecision.Deny("工具不在允許清單：" + key);
        }

        if (RequiresConfirm(key) && !IsConfirmed(args))
        {
            return McpDecision.Confirm(
                key + " 是破壞性操作，需要確認。請先向使用者確認，再以 confirm=true 重試。");
        }

        return McpDecision.Ok();
    }

    public string DoctorLine()
    {
        var allow = Allow.Count == 0 ? "全部" : string.Join(", ", Allow);
        var deny = Deny.Count == 0 ? "無" : string.Join(", ", Deny);
        var confirm = Confirm.Count == 0 ? "無" : string.Join(", ", Confirm);
        var mode = ReadOnly ? "唯讀" : "可寫";
        return $"MCP 政策：{mode}；允許 {allow}；禁止 {deny}；需確認 {confirm}（{Source}）";
    }

    public static bool IsConfirmed(IReadOnlyDictionary<string, string?>? args)
    {
        if (args is null)
            return false;
        foreach (var key in new[] { "confirm", "confirmed" })
        {
            if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;
            var v = raw.Trim();
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("y", StringComparison.OrdinalIgnoreCase)
                || v == "1")
                return true;
        }

        return false;
    }

    public static IReadOnlyList<string> SplitNames(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];
        return raw.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Norm)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static IReadOnlyList<string> ParseNames(JsonNode? node)
    {
        if (node is JsonArray arr)
        {
            return arr.Select(n => Norm(JsonUtil.Str(n)))
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return SplitNames(JsonUtil.Str(node));
    }

    static bool Contains(IEnumerable<string> names, string key) =>
        names.Any(n => string.Equals(n, key, StringComparison.OrdinalIgnoreCase));

    static string Norm(string? name) => (name ?? "").Trim().ToLowerInvariant();

    static bool Truthy(JsonNode? node)
    {
        try
        {
            return node?.GetValue<bool>() ?? false;
        }
        catch (InvalidOperationException)
        {
            var s = JsonUtil.Str(node);
            return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
        }
    }
}
