namespace AiProject.Console.Core.Stack;

/// <summary>
/// 值班一眼摘要：就緒、離線、需重編。控制台列與 <c>duty_summary</c> 共用。
/// 政策擋下的 MCP 呼叫不列入警報——那是對帳單，不是這扇窗能修的事故。
/// </summary>
public static class DutySummary
{
    public static string Attention(int offline, int staleProjects, int auditIncidents, string? lastIncidentTool)
    {
        if (IsClear(offline, staleProjects, auditIncidents))
            return "堆疊正常";

        var bits = new List<string>();
        if (offline > 0)
            bits.Add($"離線 {offline}");
        if (staleProjects > 0)
            bits.Add($"需重編 {staleProjects}");
        if (auditIncidents > 0)
        {
            bits.Add(string.IsNullOrEmpty(lastIncidentTool)
                ? $"MCP 失敗 {auditIncidents}"
                : $"MCP 失敗 {auditIncidents}（最近 {lastIncidentTool}）");
        }

        return string.Join(" · ", bits);
    }

    public static bool IsClear(int offline, int staleProjects, int auditIncidents) =>
        offline <= 0 && staleProjects <= 0 && auditIncidents <= 0;
}
