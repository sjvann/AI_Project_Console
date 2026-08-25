namespace AiProject.Console.Core.Stack;

public sealed record StackToolSpec(string Name, string Description, string ArgsHint);

/// <summary>
/// 把自然語言 Agent 的工具呼叫對到 <see cref="StackWorkspace"/>。
/// </summary>
public static class StackToolRouter
{
    public static readonly IReadOnlyList<StackToolSpec> Tools =
    [
        new("stack_status", "堆疊總覽：就緒數、需重編、各服務線上／離線。開發管理者晨會或值班第一個工具。", ""),
        new("list_services", "列出 ai-project 服務 id、port、宿主。", ""),
        new("list_projects", "列出專案編譯狀態與需重編原因。", ""),
        new("build_freshness", "與 list_projects 相同，強調編譯可信度。", ""),
        new("build", "編譯。mode=stale（預設）／services／projects／one。one 時必填 path。", "mode path"),
        new("start_service", "啟動一個服務。", "id"),
        new("stop_service", "停止一個服務。", "id"),
        new("start_all", "啟動尚未在線的服務。", ""),
        new("stop_all", "停止全部服務。", ""),
        new("get_log", "讀取服務 Log 尾端。", "id tail"),
        new("doctor", "環境體檢（dotnet／git／Agent 後端）。", ""),
        new("git_status", "目前分支、未提交、領先／落後。", ""),
    ];

    public static async Task<string> InvokeAsync(
        StackWorkspace workspace,
        string name,
        IReadOnlyDictionary<string, string?>? args = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        args ??= new Dictionary<string, string?>();
        var key = (name ?? "").Trim().ToLowerInvariant();
        try
        {
            return key switch
            {
                "stack_status" => await workspace.StackStatusAsync().ConfigureAwait(false),
                "list_services" => workspace.ListServices(),
                "list_projects" or "build_freshness" => workspace.ListProjects(),
                "build" => await workspace.BuildAsync(Arg(args, "mode") ?? "stale", Arg(args, "path")).ConfigureAwait(false),
                "start_service" => workspace.StartService(Require(args, "id")),
                "stop_service" => workspace.StopService(Require(args, "id")),
                "start_all" => await workspace.StartAllAsync().ConfigureAwait(false),
                "stop_all" => workspace.StopAll(),
                "get_log" => workspace.GetLog(Require(args, "id"), ParseTail(Arg(args, "tail"))),
                "doctor" => workspace.Doctor(),
                "git_status" => await workspace.GitStatusAsync().ConfigureAwait(false),
                _ => "{\"ok\":false,\"error\":\"未知工具：" + key + "。請先 stack_status 或看 tools 清單。\"}",
            };
        }
        catch (Exception ex)
        {
            return "{\"ok\":false,\"error\":" + System.Text.Json.JsonSerializer.Serialize(ex.Message) + "}";
        }
    }

    static string? Arg(IReadOnlyDictionary<string, string?> args, string key) =>
        args.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    static string Require(IReadOnlyDictionary<string, string?> args, string key) =>
        Arg(args, key) ?? throw new ArgumentException("缺少參數：" + key);

    static int ParseTail(string? raw) =>
        int.TryParse(raw, out var n) && n > 0 ? n : 80;
}
