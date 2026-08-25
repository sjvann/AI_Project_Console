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
    bool CanCloseIde { get; }
    AgentDetectResult Detect(string? cliOverride = null);
    string? OpenWorkspace(string root, string? cliOverride = null);
    Task<string?> LaunchAgent(string root, string prompt, string? cliOverride = null);
    string? CloseIde();
}
