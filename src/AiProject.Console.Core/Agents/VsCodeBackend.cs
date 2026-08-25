using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed class VsCodeBackend : IAgentBackend
{
    public string Id => "vscode";
    public string DisplayName => "VS Code / GitHub Copilot";
    public AgentBackendKind Kind => AgentBackendKind.Ide;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => false;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cli = Resolve(cliOverride);
        return cli is null
            ? new AgentDetectResult(false, "找不到 code CLI（VS Code：在命令選擇區執行 Shell Command: Install 'code'）")
            : new AgentDetectResult(true, "可用 — 開啟工作區並複製提示（請在 Copilot Chat 貼上）—" + cli, cli);
    }

    public string? OpenWorkspace(string root, string? cliOverride = null)
    {
        var detect = Detect(cliOverride);
        if (!detect.Available || detect.CliPath is null)
            return detect.Summary;
        return OpenFolder(detect.CliPath, root);
    }

    public Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null)
    {
        AgentPromptStore.Write(root, prompt);
        return Task.FromResult(OpenWorkspace(root, cliOverride));
    }

    public string? CloseIde() => null;

    static string? OpenFolder(string cli, string root)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--reuse-window");
            psi.ArgumentList.Add(Path.GetFullPath(root));
            System.Diagnostics.Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return $"無法開啟 VS Code：{ex.Message}";
        }
    }

    static string? Resolve(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride) && File.Exists(cliOverride))
            return cliOverride;
        return CliUtil.FindOnPath("code") ?? CliUtil.FindOnPath("code.cmd");
    }
}
