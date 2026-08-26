using System.Net;
using System.Text;
using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Stack;

namespace AiProject.Console.Core.Tests;

public class ProjectAskTests
{
    [Fact]
    public void Rank_HighlightsDutyProblems_First()
    {
        var green = ProjectAskPrompts.Rank(0, 0, 0);
        Assert.Equal(ProjectAskPrompts.Catalog.Select(c => c.Id), green.Select(v => v.Id));
        Assert.All(green, v => Assert.False(v.Highlighted));

        var ranked = ProjectAskPrompts.Rank(offline: 2, staleProjects: 1, auditFails: 3);
        Assert.Equal(["offline", "stale", "audit", "handoff", "doctor", "git"], ranked.Select(v => v.Id));
        Assert.True(ranked[0].Highlighted);
        Assert.True(ranked[1].Highlighted);
        Assert.True(ranked[2].Highlighted);
        Assert.False(ranked[3].Highlighted);
    }

    [Fact]
    public void SystemPrompt_MapsFreshToNoRebuild()
    {
        var prompt = ProjectAskService.SystemPrompt("Demo", @"E:\demo", """{"staleProjects":0}""");
        Assert.Contains("無需重編", prompt);
        Assert.Contains("staleProjects", prompt);
        Assert.Contains("\"staleProjects\":0", prompt);
    }

    [Fact]
    public void AskTools_AreReadOnly()
    {
        foreach (var tool in ProjectAskService.AskTools)
            Assert.Contains(tool.Name, McpPolicy.ReadOnlyTools);
        Assert.DoesNotContain(ProjectAskService.AskTools, t => t.Name == "build");
        Assert.DoesNotContain(ProjectAskService.AskTools, t => t.Name == "stop_all");
    }

    [Fact]
    public void ChatCompletionsUrl_AppendsPath()
    {
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", ProjectAskService.ChatCompletionsUrl("http://127.0.0.1:11434/v1"));
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", ProjectAskService.ChatCompletionsUrl("http://127.0.0.1:11434/v1/"));
        Assert.Equal("https://api.openai.com/v1/chat/completions", ProjectAskService.ChatCompletionsUrl("https://api.openai.com/v1/chat/completions"));
        Assert.False(ProjectAskService.IsConfigured("", "llama3.2"));
        Assert.True(ProjectAskService.IsConfigured(ProjectAskService.DefaultBaseUrl, ProjectAskService.DefaultModel));
    }

    [Fact]
    public async Task AskAsync_CallsLocalTool_ThenReturnsText()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var handler = new QueueHandler(
                """
                {"choices":[{"message":{"role":"assistant","tool_calls":[{"id":"c1","type":"function","function":{"name":"stack_status","arguments":"{}"}}]}}]}
                """,
                """
                {"choices":[{"message":{"role":"assistant","content":"目前可交班。"}}]}
                """);
            var reply = await ProjectAskService.AskAsync(
                ws,
                "現在能不能交班？",
                [],
                new ProjectAskOptions("http://127.0.0.1:9/v1", "test-model"),
                handler: handler);
            Assert.Equal("目前可交班。", reply);
            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("stack_status", handler.Requests[0]);
            Assert.Contains("現在能不能交班", handler.Requests[0]);
            Assert.Contains("c1", handler.Requests[1]);
            Assert.Contains("tool", handler.Requests[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    sealed class QueueHandler : HttpMessageHandler
    {
        readonly Queue<string> _replies;
        public List<string> Requests { get; } = [];

        public QueueHandler(params string[] replies) => _replies = new Queue<string>(replies);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_replies.Dequeue(), Encoding.UTF8, "application/json"),
            };
        }
    }

    static string CreateMini()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-ask-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", "Mini.Api");
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        Directory.CreateDirectory(Path.Combine(proj, "bin", "Debug", "net8.0"));
        File.WriteAllText(Path.Combine(proj, "Mini.Api.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(proj, "Program.cs"), "Console.WriteLine();");
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
            { "profiles": { "http": { "applicationUrl": "http://localhost:5998" } } }
            """);
        File.WriteAllBytes(Path.Combine(proj, "bin", "Debug", "net8.0", "Mini.Api.dll"), [0]);
        return root;
    }
}
