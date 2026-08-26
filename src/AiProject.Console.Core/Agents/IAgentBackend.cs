namespace AiProject.Console.Core.Agents;

public enum AgentBackendKind
{
    Ide,
    Cli,
    Cloud,
    Custom,
}

public sealed record AgentDetectResult(bool Available, string Summary, string? CliPath = null);

public interface IAgentBackend
{
    string Id { get; }
    string DisplayName { get; }
    AgentBackendKind Kind { get; }
    bool CanOpenWorkspace { get; }
    bool CanLaunchAgent { get; }

    /// <summary>
    /// 此後端會開啟本機 IDE 或終端機時為 true；雲端後端為 false，離開／關閉專案時不詢問關閉。
    /// </summary>
    bool CanCloseIde { get; }
    AgentDetectResult Detect(string? cliOverride = null);
    string? OpenWorkspace(string root, string? cliOverride = null);
    Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null);
    string? CloseIde();
}
