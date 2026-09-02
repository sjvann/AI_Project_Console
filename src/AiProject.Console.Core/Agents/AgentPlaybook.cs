namespace AiProject.Console.Core.Agents;

/// <summary>
/// 求救提示的 Agentic 附錄：要求 Agent 用 MCP 工具驗證，而不是只改檔。
/// </summary>
public static class AgentPlaybook
{
    public const string McpServerId = "ai-project-console";

    public static string VerificationHint() =>
        """

        ---
        驗證（若 IDE 已接 MCP「ai-project-console」請直接呼叫工具，不要只口述）：
        1. stack_status — 看哪些服務離線、哪些專案需重編
        2. 改完程式後 build（mode=stale 或 mode=one + path）
        3. 執行問題用 get_log；需要時 start_service / start_all（start_service 會先起 dependsOn）
        4. 最後再 stack_status 確認就緒數上升、需重編為 0
        5. stop_all 是破壞性操作：先問使用者，再帶 confirm=true
        沒有 MCP 時，請在控制台按「編譯過期項目／啟動」並回報結果。
        """;

    public static string ManagerHint() =>
        """
        開發管理者可用同一組 MCP 工具做值班：晨會先 duty_summary（不行再 stack_status + git_status）；
        遠端 CI 一眼用 ci_status，PR 能不能請人審用 pr_status（不要叫 Agent 去翻 Actions／PR 畫面）；事故先 get_log 再決定重啟；發版前 build mode=stale 必須全過。
        企業政策：mcp-policy.json 可白名單／唯讀；stop_all 預設需 confirm=true；對帳看 list_audit 或控制台「MCP 審計」。
        建議在 Cursor／Claude Code 並排接 GitHub MCP、Context7（文件），本控制台負責本機堆疊與 CI 是否通過。
        """;
}
