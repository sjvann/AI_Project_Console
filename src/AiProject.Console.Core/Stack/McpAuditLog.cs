using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Stack;

public sealed record McpAuditEntry(
    DateTimeOffset Utc,
    string Tool,
    bool Ok,
    long Ms,
    string ArgsText,
    string? Error)
{
    public string WhenText
    {
        get
        {
            var local = Utc.ToLocalTime();
            return local.Date == DateTime.Today
                ? local.ToString("HH:mm:ss")
                : local.ToString("MM-dd HH:mm");
        }
    }

    public string StatusText => Ok ? "成功" : "拒絕／失敗";
}

/// <summary>
/// 每次 MCP／工具呼叫寫入 <c>.ai_project/mcp-audit.jsonl</c>，給開發管理者事後對帳。
/// </summary>
public static class McpAuditLog
{
    public static string FilePath(ProjectRuntime runtime) =>
        Path.Combine(runtime.Base, "mcp-audit.jsonl");

    public static void Append(
        ProjectRuntime runtime,
        string tool,
        IReadOnlyDictionary<string, string?>? args,
        bool ok,
        string? error,
        long durationMs)
    {
        try
        {
            runtime.Ensure();
            var entry = new JsonObject
            {
                ["utc"] = DateTimeOffset.UtcNow.ToString("o"),
                ["tool"] = tool ?? "",
                ["ok"] = ok,
                ["ms"] = durationMs,
                ["args"] = SanitizeArgs(args),
            };
            if (!string.IsNullOrEmpty(error))
                entry["error"] = Truncate(error, 300);
            File.AppendAllText(FilePath(runtime), entry.ToJsonString(Compact) + "\n");
        }
        catch
        {
            // 審計失敗不阻擋工具
        }
    }

    public static (int Total, IReadOnlyList<McpAuditEntry> Entries) ReadRecent(ProjectRuntime runtime, int tail = 80)
    {
        var path = FilePath(runtime);
        if (!File.Exists(path))
            return (0, []);

        var lines = File.ReadAllLines(path);
        if (tail < 1)
            tail = 80;
        var slice = lines.Length > tail ? lines[^tail..] : lines;
        var entries = new List<McpAuditEntry>(slice.Length);
        foreach (var line in slice)
        {
            if (TryParse(line) is { } entry)
                entries.Add(entry);
        }

        return (lines.Length, entries);
    }

    public static string ListRecent(ProjectRuntime runtime, int tail = 50)
    {
        var path = FilePath(runtime);
        var (count, entries) = ReadRecent(runtime, tail);
        if (count == 0)
            return Json(new { path, count = 0, entries = Array.Empty<object>(), message = "尚無審計紀錄" });

        return Json(new
        {
            path,
            count,
            entries = entries.Select(e => new
            {
                utc = e.Utc.ToString("o"),
                tool = e.Tool,
                ok = e.Ok,
                ms = e.Ms,
                args = e.ArgsText,
                error = e.Error,
            }),
        });
    }

    public static bool LooksSuccessful(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;
        try
        {
            var node = JsonNode.Parse(result);
            if (node is JsonObject obj && obj["ok"] is not null)
                return obj["ok"]!.GetValue<bool>();
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return !result.Contains("\"ok\":false", StringComparison.Ordinal)
            && !result.Contains("\"ok\": false", StringComparison.Ordinal);
    }

    public static string? ExtractError(string result)
    {
        try
        {
            if (JsonNode.Parse(result) is JsonObject obj)
                return JsonUtil.Str(obj["error"]);
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    public static McpAuditEntry? TryParse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject obj)
                return null;
            if (!DateTimeOffset.TryParse(JsonUtil.Str(obj["utc"]), out var utc))
                utc = DateTimeOffset.UtcNow;
            var ok = false;
            try { ok = obj["ok"]?.GetValue<bool>() ?? false; }
            catch (InvalidOperationException) { /* leave false */ }
            long ms = 0;
            try { ms = obj["ms"]?.GetValue<long>() ?? 0; }
            catch (InvalidOperationException)
            {
                _ = long.TryParse(JsonUtil.Str(obj["ms"]), out ms);
            }

            return new McpAuditEntry(
                utc,
                JsonUtil.Str(obj["tool"]),
                ok,
                ms,
                FormatArgs(obj["args"]),
                string.IsNullOrEmpty(JsonUtil.Str(obj["error"])) ? null : JsonUtil.Str(obj["error"]));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string FormatArgs(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            return string.Join("  ", obj
                .Select(kv => $"{kv.Key}={JsonUtil.Str(kv.Value)}")
                .Where(s => !s.EndsWith('=')));
        }

        return JsonUtil.Str(node);
    }

    static JsonObject SanitizeArgs(IReadOnlyDictionary<string, string?>? args)
    {
        var obj = new JsonObject();
        if (args is null)
            return obj;
        foreach (var kv in args)
            obj[kv.Key] = Truncate(kv.Value ?? "", 200);
        return obj;
    }

    static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        Encoder = JsonUtil.Options.Encoder,
    };

    static string Json(object value) => JsonSerializer.Serialize(value, JsonUtil.Options);
}
