namespace AiProject.Console.Core.Stack;

/// <summary>
/// 值班一眼摘要：就緒、需重編、最近 MCP 拒絕。控制台列與 <c>duty_summary</c> 共用。
/// </summary>
public static class DutySummary
{
    public static string Attention(int offline, int staleProjects, int auditFails, string? lastFailTool)
    {
        if (offline <= 0 && staleProjects <= 0 && auditFails <= 0)
            return "堆疊正常";

        var bits = new List<string>();
        if (offline > 0)
            bits.Add($"離線 {offline}");
        if (staleProjects > 0)
            bits.Add($"需重編 {staleProjects}");
        if (auditFails > 0)
        {
            bits.Add(string.IsNullOrEmpty(lastFailTool)
                ? $"MCP 拒絕 {auditFails}"
                : $"MCP 拒絕 {auditFails}（最近 {lastFailTool}）");
        }

        return string.Join(" · ", bits);
    }

    public static bool IsClear(int offline, int staleProjects, int auditFails) =>
        offline <= 0 && staleProjects <= 0 && auditFails <= 0;
}
