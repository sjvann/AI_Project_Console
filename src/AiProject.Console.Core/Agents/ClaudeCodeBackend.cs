using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed class ClaudeCodeBackend : IAgentBackend
{
    public const int PromptMaxChars = 5000;
    public const string DeeplinkBase = "claude-cli://open";

    public string Id => "claude";
    public string DisplayName => "Claude Code";
    public AgentBackendKind Kind => AgentBackendKind.Cli;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => true;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cli = Resolve(cliOverride);
        return cli is null
            ? new AgentDetectResult(false, "找不到 claude（請安裝 Claude Code CLI）")
            : new AgentDetectResult(true, "可用 — " + cli, cli);
    }

    public string? OpenWorkspace(string root, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available || detect.CliPath is null)
            return detect.Summary;
        return TerminalSession.Start(detect.CliPath, [], root, "Claude Code");
    }

    public Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null) =>
        Task.FromResult(Launch(root, prompt, cliOverride));

    string? Launch(string root, string prompt, string? cliOverride)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available)
            return detect.Summary;
        var text = prompt ?? "";
        if (text.Length > PromptMaxChars)
        {
            var file = AgentPromptStore.Write(root, text);
            text = "請讀取此檔並依內容處理：\n" + file;
        }
        var url = DeeplinkBase
            + "?cwd=" + Uri.EscapeDataString(Path.GetFullPath(root))
            + "&q=" + Uri.EscapeDataString(text);
        return TerminalSession.OpenUri(url);
    }

    public string? CloseIde()
    {
        TerminalSession.CloseStarted();
        return LocalAppCloser.Close(
            DisplayName,
            ["claude", "Claude"],
            windowsImages: ["claude.exe"],
            unixPattern: "claude",
            windowTitlePrefix: "Claude Code");
    }

    static string? Resolve(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride) && File.Exists(cliOverride))
            return cliOverride;
        return CliUtil.FindOnPath("claude");
    }
}
