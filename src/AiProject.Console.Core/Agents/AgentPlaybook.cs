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
        3. 執行問題用 get_log；需要時 start_service / start_all
        4. 最後再 stack_status 確認就緒數上升、需重編為 0
        沒有 MCP 時，請在控制台按「編譯過期項目／啟動」並回報結果。
        """;

    public static string ManagerHint() =>
        """
        開發管理者可用同一組 MCP 工具做值班：晨會先 stack_status + git_status；
        事故先 get_log 再決定重啟；發版前 build mode=stale 必須全過。
        建議在 Cursor／Claude Code 並排接 GitHub MCP、Context7（文件），本控制台只負責本機堆疊真相。
        """;
}
