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
        new("duty_summary", "值班一眼摘要：就緒、離線、需重編。政策擋下的 MCP 呼叫不列入警報。晨會第一個工具。", ""),
        new("stack_status", "堆疊總覽：就緒數、需重編、各服務線上／離線。開發管理者晨會或值班第一個工具。", ""),
        new("list_services", "列出 ai-project 服務 id、port、宿主與一句用途。", ""),
        new("list_projects", "列出各專案編譯徽章、一句用途，與需重編數字。needsRebuild=0 或 badge=最新 表示無需重編；不要把「已與 DLL 一致」理解成要重編。", ""),
        new("build_freshness", "與 list_projects 相同：編譯徽章與需重編數字。fresh／最新＝無需重編。", ""),
        new("build", "編譯。mode=stale（預設）／services／projects／one。one 時必填 path。", "mode path"),
        new("start_service", "啟動一個服務。預設先起 dependsOn 並等到就緒。skipDepends=true 只起自己；skipOptional=true 略過可選相依。", "id skipDepends skipOptional"),
        new("stop_service", "停止一個服務。可在政策 confirm 列入後要求 confirm=true。", "id confirm"),
        new("start_all", "啟動尚未在線的服務（依 startOrder，並先起 dependsOn）。", ""),
        new("stop_all", "停止全部服務。破壞性操作：必須先向使用者確認，再帶 confirm=true。", "confirm"),
        new("get_log", "讀取服務 Log 尾端。", "id tail"),
        new("doctor", "環境體檢（git、依專案偵測的語言環境／Agent 後端／MCP 政策）。", ""),
        new("git_status", "目前分支、未提交、領先／落後。", ""),
        new("ci_status", "遠端 GitHub Actions 最近執行：通過／失敗／進行中。只摘要，不含 log。", ""),
        new("pr_status", "目前分支的 PR 與檢查是否通過。只回答能不能請人審，不含審查內容。", ""),
        new("list_audit", "讀取最近的 MCP 審計紀錄（.ai_project/mcp-audit.jsonl）。", "tail"),
        new("docs_status", "文件體系狀態：docs/ 是否存在、骨架、待補頁數、DocFX／Pages workflow。", ""),
        new("list_docs", "列出 docs/ 內的 Markdown 與 DocFX 設定檔。", ""),
        new("read_doc", "讀取 docs/ 內一份檔案。path 為相對 docs/ 的路徑，例如 user/getting-started.md。", "path"),
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
        "ci_status" => "CI 狀態",
        "pr_status" => "PR 狀態",
        "list_audit" => "MCP 審計",
        "docs_status" => "文件狀態",
        "list_docs" => "文件清單",
        "read_doc" => "讀取文件",
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
            "start_service" => await workspace.StartServiceAsync(
                Require(args, "id"),
                skipDepends: Flag(args, "skipDepends", "skip_depends"),
                skipOptional: Flag(args, "skipOptional", "skip_optional")),
            "stop_service" => workspace.StopService(Require(args, "id")),
            "start_all" => await workspace.StartAllAsync().ConfigureAwait(false),
            "stop_all" => workspace.StopAll(),
            "get_log" => workspace.GetLog(Require(args, "id"), ParseTail(Arg(args, "tail"), 80)),
            "doctor" => workspace.Doctor(),
            "git_status" => await workspace.GitStatusAsync().ConfigureAwait(false),
            "ci_status" => await workspace.CiStatusAsync().ConfigureAwait(false),
            "pr_status" => await workspace.PrStatusAsync().ConfigureAwait(false),
            "list_audit" => McpAuditLog.ListRecent(workspace.Runtime, ParseTail(Arg(args, "tail"), 50)),
            "docs_status" => workspace.DocsStatus(),
            "list_docs" => workspace.ListDocs(),
            "read_doc" => workspace.ReadDoc(Require(args, "path")),
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

    static bool Flag(IReadOnlyDictionary<string, string?> args, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = Arg(args, key);
            if (v is null)
                continue;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("y", StringComparison.OrdinalIgnoreCase)
                || v == "1")
                return true;
        }
        return false;
    }

    static string Require(IReadOnlyDictionary<string, string?> args, string key) =>
        Arg(args, key) ?? throw new ArgumentException("缺少參數：" + key);

    static int ParseTail(string? raw, int fallback)
    {
        if (int.TryParse(raw, out var n) && n > 0)
            return n;
        return fallback;
    }
}
