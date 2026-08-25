namespace AiProject.Console.Core.Agents;

public sealed class CustomCommandBackend : IAgentBackend
{
    public string Id => "custom";
    public string DisplayName => "自訂命令";
    public AgentBackendKind Kind => AgentBackendKind.Custom;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => false;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cmd = (cliOverride ?? "").Trim();
        if (string.IsNullOrEmpty(cmd))
            return new AgentDetectResult(false, "尚未設定自訂命令（請在設定填寫命令範本）");
        return new AgentDetectResult(true, "將執行：" + cmd, cmd);
    }

    public string? OpenWorkspace(string root, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available)
            return detect.Summary;
        return RunTemplate(detect.CliPath!, root, prompt: "", launch: false);
    }

    public Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available)
            return Task.FromResult<string?>(detect.Summary);
        return Task.FromResult(RunTemplate(detect.CliPath!, root, prompt ?? "", launch: true));
    }

    public string? CloseIde() => null;

    public static string[] SplitCommand(string expanded)
    {
        var parts = new List<string>();
        var current = "";
        var quote = '\0';
        foreach (var ch in expanded)
        {
            if (quote != '\0')
            {
                if (ch == quote)
                    quote = '\0';
                else
                    current += ch;
                continue;
            }
            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    parts.Add(current);
                    current = "";
                }
                continue;
            }
            current += ch;
        }
        if (current.Length > 0)
            parts.Add(current);
        return parts.ToArray();
    }

    static string? RunTemplate(string template, string root, string prompt, bool launch)
    {
        var file = launch || template.Contains("{promptFile}", StringComparison.OrdinalIgnoreCase)
            ? AgentPromptStore.Write(root, prompt)
            : "";
        var expanded = TerminalSession.Expand(template, root, prompt, file);
        var parts = SplitCommand(expanded);
        if (parts.Length == 0)
            return "自訂命令是空的";
        return TerminalSession.Start(parts[0], parts.Skip(1).ToArray(), root, "自訂 Agent");
    }
}
