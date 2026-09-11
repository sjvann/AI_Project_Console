using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed class WindsurfBackend : IAgentBackend
{
    public string Id => "windsurf";
    public string DisplayName => "Windsurf / Devin Desktop";
    public AgentBackendKind Kind => AgentBackendKind.Ide;
    public bool CanOpenWorkspace => true;
    public bool CanLaunchAgent => true;
    public bool CanCloseIde => true;

    public AgentDetectResult Detect(string? cliOverride = null)
    {
        var cli = Resolve(cliOverride);
        return cli is null
            ? new AgentDetectResult(false, "找不到 windsurf／devin CLI")
            : new AgentDetectResult(true, "可用 — 開啟工作區並複製提示 — " + cli, cli);
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

    public string? CloseIde(string? workspaceRoot = null) => LocalAppCloser.Close(
        DisplayName,
        ["Windsurf", "windsurf", "Devin"],
        windowsImages: workspaceRoot is null ? ["Windsurf.exe", "Devin.exe"] : null,
        macAppNames: ["Windsurf", "Devin"],
        unixPattern: workspaceRoot is null ? "windsurf" : null,
        workspaceRoot: workspaceRoot);

    static string? OpenFolder(string cli, string root)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(cli)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(Path.GetFullPath(root));
            System.Diagnostics.Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return $"無法開啟 Windsurf：{ex.Message}";
        }
    }

    static string? Resolve(string? cliOverride)
    {
        if (!string.IsNullOrWhiteSpace(cliOverride) && File.Exists(cliOverride))
            return cliOverride;
        foreach (var name in new[] { "windsurf", "devin", "devin-desktop" })
        {
            var found = CliUtil.FindOnPath(name);
            if (found is not null)
                return found;
        }
        if (!OperatingSystem.IsWindows())
            return null;
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        foreach (var rel in new[]
        {
            Path.Combine("Programs", "Windsurf", "bin", "windsurf.cmd"),
            Path.Combine("Programs", "Devin", "bin", "devin.cmd"),
        })
        {
            var cmd = Path.Combine(local, rel);
            if (File.Exists(cmd))
                return cmd;
        }
        return null;
    }
}
