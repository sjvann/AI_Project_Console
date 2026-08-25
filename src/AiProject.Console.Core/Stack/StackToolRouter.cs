using System.Diagnostics;
using System.Text.Json;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Stack;

public sealed record StackToolSpec(string Name, string Description, string ArgsHint);

/// <summary>
/// 把自然語言 Agent 的工具呼叫對到 <see cref="StackWorkspace"/>。
/// </summary>
public static class StackToolRouter
{
    public static readonly IReadOnlyList<StackToolSpec> Tools =
    [
        new("duty_summary", "值班一眼摘要：就緒、離線、需重編、最近 MCP 拒絕。晨會第一個工具。", ""),
        new("stack_status", "堆疊總覽：就緒數、需重編、各服務線上／離線。開發管理者晨會或值班第一個工具。", ""),
        new("list_services", "列出 ai-project 服務 id、port、宿主。", ""),
        new("list_projects", "列出專案編譯狀態與需重編原因。", ""),
        new("build_freshness", "與 list_projects 相同，強調編譯可信度。", ""),
        new("build", "編譯。mode=stale（預設）／services／projects／one。one 時必填 path。", "mode path"),
        new("start_service", "啟動一個服務。", "id"),
        new("stop_service", "停止一個服務。可在政策 confirm 列入後要求 confirm=true。", "id confirm"),
        new("start_all", "啟動尚未在線的服務。", ""),
        new("stop_all", "停止全部服務。破壞性操作：必須先向使用者確認，再帶 confirm=true。", "confirm"),
        new("get_log", "讀取服務 Log 尾端。", "id tail"),
        new("doctor", "環境體檢（dotnet／git／Agent 後端／MCP 政策）。", ""),
        new("git_status", "目前分支、未提交、領先／落後。", ""),
        new("list_audit", "讀取最近的 MCP 審計紀錄（.ai_project/mcp-audit.jsonl）。", "tail"),
    ];

    public static string DisplayTitle(string name) => name switch
    {
        "duty_summary" => "值班摘要",
        "stack_status" => "堆疊總覽",
        "list_services" => "服務清單",
        "list_projects" => "專案編譯狀態",
        "build_freshness" => "編譯可信度",
        "build" => "編譯",
        "start_service" => "啟動服務",
        "stop_service" => "停止服務",
        "start_all" => "啟動全部",
        "stop_all" => "停止全部",
        "get_log" => "讀取 Log",
        "doctor" => "環境體檢",
        "git_status" => "Git 狀態",
        "list_audit" => "MCP 審計",
        _ => name,
    };

    public static IEnumerable<StackToolSpec> VisibleTools(McpPolicy policy) =>
        Tools.Where(t => policy.IsAllowed(t.Name));

    public static async Task<string> InvokeAsync(
        StackWorkspace workspace,
        string name,
        IReadOnlyDictionary<string, string?>? args = null,
        McpPolicy? policy = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        args ??= new Dictionary<string, string?>();
        policy ??= McpPolicy.Load(workspace.Root);
        var key = (name ?? "").Trim().ToLowerInvariant();
        var sw = Stopwatch.StartNew();
        string result;
        try
        {
            if (!Tools.Any(t => t.Name == key))
            {
                result = Fail("未知工具：" + key + "。請先 stack_status 或看 tools 清單。");
            }
            else
            {
                var decision = policy.Decide(key, args);
                result = decision.Allowed
                    ? await DispatchAsync(workspace, key, args).ConfigureAwait(false)
                    : Fail(decision.Error ?? "政策拒絕", decision.NeedConfirm, key);
            }
        }
        catch (Exception ex)
        {
            result = Fail(ex.Message);
        }

        McpAuditLog.Append(
            workspace.Runtime,
            key,
            args,
            McpAuditLog.LooksSuccessful(result),
            McpAuditLog.ExtractError(result),
            sw.ElapsedMilliseconds);
        return result;
    }

    static async Task<string> DispatchAsync(
        StackWorkspace workspace,
        string key,
        IReadOnlyDictionary<string, string?> args) =>
        key switch
        {
            "duty_summary" => await workspace.DutySummaryAsync().ConfigureAwait(false),
            "stack_status" => await workspace.StackStatusAsync().ConfigureAwait(false),
            "list_services" => workspace.ListServices(),
            "list_projects" or "build_freshness" => workspace.ListProjects(),
            "build" => await workspace.BuildAsync(Arg(args, "mode") ?? "stale", Arg(args, "path")).ConfigureAwait(false),
            "start_service" => workspace.StartService(Require(args, "id")),
            "stop_service" => workspace.StopService(Require(args, "id")),
            "start_all" => await workspace.StartAllAsync().ConfigureAwait(false),
            "stop_all" => workspace.StopAll(),
            "get_log" => workspace.GetLog(Require(args, "id"), ParseTail(Arg(args, "tail"), 80)),
            "doctor" => workspace.Doctor(),
            "git_status" => await workspace.GitStatusAsync().ConfigureAwait(false),
            "list_audit" => McpAuditLog.ListRecent(workspace.Runtime, ParseTail(Arg(args, "tail"), 50)),
            _ => Fail("未知工具：" + key),
        };

    static string Fail(string error, bool needConfirm = false, string? tool = null)
    {
        object payload = needConfirm
            ? new { ok = false, error, needConfirm = true, tool }
            : new { ok = false, error };
        return JsonSerializer.Serialize(payload, JsonUtil.Options);
    }

    static string? Arg(IReadOnlyDictionary<string, string?> args, string key) =>
        args.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    static string Require(IReadOnlyDictionary<string, string?> args, string key) =>
        Arg(args, key) ?? throw new ArgumentException("缺少參數：" + key);

    static int ParseTail(string? raw, int fallback)
    {
        if (int.TryParse(raw, out var n) && n > 0)
            return n;
        return fallback;
    }
}
