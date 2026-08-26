using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Build;
using AiProject.Console.Core.Runtime;

namespace AiProject.Console.Core.Tests;

public class AgentBackendTests
{
    [Fact]
    public void Registry_DefaultsToCursor()
    {
        var backend = AgentBackendRegistry.Get(null);
        Assert.Equal("cursor", backend.Id);
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "claude");
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "aider");
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "codex");
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "custom");
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "vscode");
        Assert.Contains(AgentBackendRegistry.All, b => b.Id == "windsurf");
    }

    [Fact]
    public void LocalBackends_OfferCloseOnLeave()
    {
        foreach (var backend in AgentBackendRegistry.All)
        {
            Assert.NotEqual(AgentBackendKind.Cloud, backend.Kind);
            Assert.True(backend.CanCloseIde, backend.Id + " 會開本機應用程式，離開時應詢問關閉");
        }
    }

    [Fact]
    public void LocalAppCloser_UnknownProcess_IsNotRunning()
    {
        Assert.False(LocalAppCloser.IsRunning("this-process-should-not-exist-ai-console-xyz"));
        Assert.False(LocalAppCloser.HasWindowTitle("___ai-console-no-such-window___"));
    }

    [Fact]
    public void CustomCommand_SplitsQuotedArgs()
    {
        var parts = CustomCommandBackend.SplitCommand("my-agent --workspace \"C:\\proj dir\" --file x.md");
        Assert.Equal(["my-agent", "--workspace", "C:\\proj dir", "--file", "x.md"], parts);
    }

    [Fact]
    public void TerminalSession_ExpandsPlaceholders()
    {
        var text = TerminalSession.Expand("run --cwd {root} --in {promptFile}", "E:\\app", "hello", "E:\\app\\.ai_project\\p.md");
        Assert.Contains("E:\\app", text);
        Assert.Contains("p.md", text);
        Assert.DoesNotContain("{root}", text);
    }

    [Fact]
    public void CustomDetect_RequiresCommand()
    {
        var backend = new CustomCommandBackend();
        Assert.False(backend.Detect("").Available);
        Assert.True(backend.Detect("aider --message-file {promptFile}").Available);
    }

    [Fact]
    public void ClaudeDeeplink_CapsPromptConstant()
    {
        Assert.Equal(5000, ClaudeCodeBackend.PromptMaxChars);
        Assert.StartsWith("claude-cli://", ClaudeCodeBackend.DeeplinkBase);
    }

    [Fact]
    public void BuildReport_WritesJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rt = new ProjectRuntime(root);
            var path = BuildReportStore.Write(rt, Path.Combine(root, "src", "Demo.Api"), 0, "Debug");
            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            Assert.Contains("\"status\": \"ok\"", json);
            Assert.Contains("Debug", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
