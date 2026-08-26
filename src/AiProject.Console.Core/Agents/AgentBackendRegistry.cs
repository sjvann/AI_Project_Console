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

    public static AgentDoctorInfo Inspect()
    {
        var current = Current();
        var currentDetect = current.Detect(CliOverrideFor(current));
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
        return new AgentDoctorInfo(
            current.Id,
            current.DisplayName,
            currentDetect.Available,
            currentDetect.Summary,
            currentDetect.CliPath,
            othersOk,
            othersMissing);
    }

    public static IReadOnlyList<string> DoctorLines()
    {
        var info = Inspect();
        var lines = new List<string>
        {
            $"目前 Agent 後端：{info.CurrentName}（{(info.Available ? "可用" : "不可用")}）",
            "  " + info.Summary,
        };
        if (info.InstalledOthers.Count > 0)
            lines.Add("其他已安裝：" + string.Join("、", info.InstalledOthers));
        if (info.MissingOthers.Count > 0)
            lines.Add("未偵測：" + string.Join("、", info.MissingOthers));
        lines.Add("可在「設定」切換後端。");
        return lines;
    }
}

public sealed record AgentDoctorInfo(
    string CurrentId,
    string CurrentName,
    bool Available,
    string Summary,
    string? CliPath,
    IReadOnlyList<string> InstalledOthers,
    IReadOnlyList<string> MissingOthers);
