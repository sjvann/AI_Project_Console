using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed class AiderBackend : IAgentBackend
{
    public string Id => "aider";
    public string DisplayName => "Aider";
    public AgentBackendKind Kind => AgentBackendKind.Cli;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => true;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cli = Resolve(cliOverride);
        return cli is null
            ? new AgentDetectResult(false, "找不到 aider（pip install aider-chat）")
            : new AgentDetectResult(true, "可用 — " + cli, cli);
    }

    public string? OpenWorkspace(string root, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available || detect.CliPath is null)
            return detect.Summary;
        return TerminalSession.Start(detect.CliPath, [], root, "Aider");
    }

    public Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null) =>
        Task.FromResult(Launch(root, prompt, cliOverride));

    string? Launch(string root, string prompt, string? cliOverride)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available || detect.CliPath is null)
            return detect.Summary;
        var text = prompt ?? "";
        if (text.Length > 1800)
        {
            var file = AgentPromptStore.Write(root, text);
            return TerminalSession.Start(detect.CliPath, ["--message-file", file], root, "Aider");
        }
        return TerminalSession.Start(detect.CliPath, ["--message", text], root, "Aider");
    }

    public string? CloseIde(string? workspaceRoot = null)
    {
        TerminalSession.CloseStarted();
        return LocalAppCloser.Close(
            DisplayName,
            ["aider"],
            windowsImages: ["aider.exe"],
            unixPattern: "aider",
            windowTitlePrefix: "Aider");
    }

    static string? Resolve(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride) && File.Exists(cliOverride))
            return cliOverride;
        return CliUtil.FindOnPath("aider");
    }
}
