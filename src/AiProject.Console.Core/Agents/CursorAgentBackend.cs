using AiProject.Console.Core.Cursor;

namespace AiProject.Console.Core.Agents;

public sealed class CursorAgentBackend : IAgentBackend
{
    public string Id => "cursor";
    public string DisplayName => "Cursor";
    public AgentBackendKind Kind => AgentBackendKind.Ide;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => true;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cli = Resolve(cliOverride);
        return cli is null
            ? new AgentDetectResult(false, "找不到 Cursor CLI（請安裝 Cursor 並把 cursor 加到 PATH）")
            : new AgentDetectResult(true, "可用 — " + cli, cli);
    }

    public string? OpenWorkspace(string root, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available)
            return detect.Summary;
        return CursorLauncher.OpenInCursor(root);
    }

    public async Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null)
    {
        var open = CursorLauncher.OpenProjectForNewAgent(root);
        if (open is not null)
            return open;
        await Task.Delay(CursorLauncher.NewAgentLaunchDelayMs()).ConfigureAwait(false);
        return CursorLauncher.OpenPromptDeeplink(prompt);
    }

    public string? CloseIde(string? workspaceRoot = null) => CursorLauncher.CloseCursor(workspaceRoot);

    static string? Resolve(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride) && File.Exists(cliOverride))
            return cliOverride;
        return CursorLauncher.ResolveCli();
    }
}
