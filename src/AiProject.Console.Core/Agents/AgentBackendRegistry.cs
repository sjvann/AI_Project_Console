using AiProject.Console.Core.Runtime;

namespace AiProject.Console.Core.Agents;

public static class AgentBackendRegistry
{
    static readonly IAgentBackend[] Backends =
    [
        new CursorAgentBackend(),
        new ClaudeCodeBackend(),
        new AiderBackend(),
        new CodexCliBackend(),
        new VsCodeBackend(),
        new WindsurfBackend(),
        new CustomCommandBackend(),
    ];

    public static IReadOnlyList<IAgentBackend> All => Backends;

    public static IAgentBackend Get(string? id)
    {
        var key = (id ?? "").Trim();
        return Backends.FirstOrDefault(b => b.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? Backends[0];
    }

    public static IAgentBackend Current() => Get(ConsoleSettingsStore.GetAgentProvider());

    public static string? CliOverrideFor(IAgentBackend backend)
    {
        if (backend.Id == "custom")
        {
            var cmd = ConsoleSettingsStore.GetCustomAgentCommand();
            var args = ConsoleSettingsStore.GetCustomAgentArgs();
            if (string.IsNullOrWhiteSpace(cmd))
                return "";
            return string.IsNullOrWhiteSpace(args) ? cmd : cmd.Trim() + " " + args.Trim();
        }
        var path = ConsoleSettingsStore.GetAgentCliPath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static AgentDetectResult DetectCurrent()
    {
        var backend = Current();
        return backend.Detect(CliOverrideFor(backend));
    }

    public static IReadOnlyList<string> DoctorLines()
    {
        var current = Current();
        var currentDetect = current.Detect(CliOverrideFor(current));
        var lines = new List<string>
        {
            $"目前 Agent 後端：{current.DisplayName}（{(currentDetect.Available ? "可用" : "不可用")}）",
            "  " + currentDetect.Summary,
        };
        var othersOk = new List<string>();
        var othersMissing = new List<string>();
        foreach (var backend in Backends)
        {
            if (backend.Id == current.Id)
                continue;
            var detect = backend.Detect(backend.Id == "custom" ? CliOverrideFor(backend) : null);
            if (detect.Available)
                othersOk.Add(backend.DisplayName);
            else
                othersMissing.Add(backend.DisplayName);
        }
        if (othersOk.Count > 0)
            lines.Add("其他已安裝：" + string.Join("、", othersOk));
        if (othersMissing.Count > 0)
            lines.Add("未偵測：" + string.Join("、", othersMissing));
        lines.Add("可在「設定」切換後端。");
        return lines;
    }
}
