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
    public void ModelsUrl_StripsChatPath()
    {
        Assert.Equal("http://127.0.0.1:11434/v1/models", ProjectAskService.ModelsUrl("http://127.0.0.1:11434/v1"));
        Assert.Equal("http://127.0.0.1:11434/v1/models", ProjectAskService.ModelsUrl("http://127.0.0.1:11434/v1/"));
        Assert.Equal("http://127.0.0.1:11434/v1/models", ProjectAskService.ModelsUrl("http://127.0.0.1:11434/v1/chat/completions"));
        Assert.Equal("https://api.openai.com/v1/models", ProjectAskService.ModelsUrl("https://api.openai.com/v1/models"));
    }

    [Fact]
    public void PullUrl_UsesOllamaNativeApi()
    {
        Assert.Equal("http://127.0.0.1:11434/api/pull", ProjectAskService.PullUrl("http://127.0.0.1:11434/v1"));
        Assert.Equal("http://127.0.0.1:11434/api/pull", ProjectAskService.PullUrl("http://127.0.0.1:11434/v1/"));
        Assert.Equal("http://127.0.0.1:11434/api/pull", ProjectAskService.PullUrl("http://127.0.0.1:11434/v1/chat/completions"));
        Assert.True(ProjectAskProviders.CanPullModels("http://127.0.0.1:11434/v1"));
        Assert.True(ProjectAskProviders.CanPullModels("http://192.168.1.8:11434/v1"));
        Assert.False(ProjectAskProviders.CanPullModels("https://api.openai.com/v1"));
    }

    [Fact]
    public void PullCandidates_SkipsInstalledAndGemma()
    {
        var missing = ProjectAskService.PullCandidates(
            "http://127.0.0.1:11434/v1",
            "qwen2.5:14b",
            ["llama3.2:latest"]);
        Assert.DoesNotContain(missing, m => m.Equals("llama3.2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("llama3.1", missing);
        Assert.Contains("qwen2.5", missing);
        Assert.Contains("qwen2.5:14b", missing);
        Assert.DoesNotContain(missing, m => m.Contains("gemma", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(ProjectAskService.PullCandidates("https://api.openai.com/v1", "gpt-4o-mini", []));
    }

    [Fact]
    public void ModelInList_MatchesTagsAndSlash()
    {
        Assert.True(ProjectAskService.ModelInList("llama3.2", ["llama3.2:latest"]));
        Assert.True(ProjectAskService.ModelInList("gpt-4o-mini", ["openai/gpt-4o-mini"]));
        Assert.False(ProjectAskService.ModelInList("llama3.2", ["qwen2.5"]));
    }

    [Fact]
    public void SupportsTools_DropsGemmaAndEmbeddings()
    {
        Assert.False(ProjectAskModelSupport.SupportsTools("gemma3"));
        Assert.False(ProjectAskModelSupport.SupportsTools("gemma3:4b"));
        Assert.False(ProjectAskModelSupport.SupportsTools("registry.ollama.ai/library/gemma3:4b"));
        Assert.False(ProjectAskModelSupport.SupportsTools("nomic-embed-text"));
        Assert.False(ProjectAskModelSupport.SupportsTools("llava:7b"));
        Assert.True(ProjectAskModelSupport.SupportsTools("llama3.2"));
        Assert.True(ProjectAskModelSupport.SupportsTools("llama3.1:8b"));
        Assert.True(ProjectAskModelSupport.SupportsTools("qwen2.5"));
        Assert.True(ProjectAskModelSupport.SupportsTools("gpt-4o-mini"));
        Assert.Contains("不支援工具呼叫", ProjectAskModelSupport.RejectReason("gemma3:4b"));
    }

    [Fact]
    public void FilterToolCapable_KeepsOnlyAskableModels()
    {
        var kept = ProjectAskModelSupport.FilterToolCapable(
            ["gemma3:4b", "llama3.2:latest", "nomic-embed-text", "qwen2.5"]);
        Assert.Equal(["llama3.2:latest", "qwen2.5"], kept);
    }

    [Fact]
    public void Providers_OllamaSuggestions_AreToolCapable()
    {
        var models = ProjectAskProviders.Get("ollama").SuggestedModels;
        Assert.DoesNotContain(models, m => m.Contains("gemma", StringComparison.OrdinalIgnoreCase));
        Assert.All(models, m => Assert.True(ProjectAskModelSupport.SupportsTools(m)));
        Assert.All(ProjectAskProviders.Catalog, p =>
            Assert.All(p.SuggestedModels, m => Assert.True(ProjectAskModelSupport.SupportsTools(m))));
    }

    [Fact]
    public void Providers_MatchLocalOllama()
    {
        Assert.Equal("ollama", ProjectAskProviders.MatchId("http://127.0.0.1:11434/v1"));
        Assert.Equal("ollama", ProjectAskProviders.MatchId("http://localhost:11434/v1/"));
        Assert.Equal("openai", ProjectAskProviders.MatchId("https://api.openai.com/v1"));
        Assert.Equal("custom", ProjectAskProviders.MatchId("https://example.internal/v1"));
        Assert.Equal("本機 Ollama", ProjectAskProviders.TitleFor("http://127.0.0.1:11434/v1"));
    }

    [Fact]
    public void Providers_Upsert_KeepsLatestFirst()
    {
        var first = ProjectAskProviders.Upsert([], "http://127.0.0.1:11434/v1", "llama3.2", null);
        var second = ProjectAskProviders.Upsert(first, "https://api.openai.com/v1", "gpt-4o-mini", "sk-test");
        Assert.Equal("openai", second[0].Id);
        Assert.Equal("ollama", second[1].Id);
        var again = ProjectAskProviders.Upsert(second, "http://127.0.0.1:11434/v1/", "llama3.1", null);
        Assert.Equal("ollama", again[0].Id);
        Assert.Equal("llama3.1", again[0].Model);
        Assert.Single(again, s => s.Id == "ollama");
    }

    [Fact]
    public async Task ProbeAsync_ListsModels_AndFlagsMissing()
    {
        var handler = new QueueHandler(HttpStatusCode.OK, """
            {"data":[{"id":"llama3.2:latest"},{"id":"qwen2.5"}]}
            """);
        var ok = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "llama3.2"),
            handler);
        Assert.True(ok.Ok);
        Assert.True(ok.ModelFound);
        Assert.Contains("來源正常", ok.Message);
        Assert.Contains("qwen2.5", ok.Models);

        var missing = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "missing-model"),
            handler: new QueueHandler(HttpStatusCode.OK, """
                {"data":[{"id":"llama3.2"}]}
                """));
        Assert.True(missing.Ok);
        Assert.False(missing.ModelFound);
        Assert.Contains("沒有 missing-model", missing.Message);
    }

    [Fact]
    public async Task ProbeAsync_EmptyModel_ListsWithoutFailing()
    {
        var result = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "", null),
            handler: new QueueHandler(HttpStatusCode.OK, """{"data":[{"id":"llama3.2"},{"id":"gemma3"}]}"""));
        Assert.True(result.Ok);
        Assert.False(result.ModelFound);
        Assert.Equal(["llama3.2"], result.Models);
        Assert.Contains("請從下方選一個", result.Message);
        Assert.Contains("已略過不支援工具的模型", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_RejectsGemmaEvenIfListed()
    {
        var result = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "gemma3:4b"),
            handler: new QueueHandler(HttpStatusCode.OK, """
                {"data":[{"id":"gemma3:4b"},{"id":"llama3.2:latest"}]}
                """));
        Assert.False(result.Ok);
        Assert.False(result.ModelFound);
        Assert.Equal(["llama3.2:latest"], result.Models);
        Assert.Contains("不支援工具呼叫", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_FailsWhenOnlyNonToolModels()
    {
        var result = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "", null),
            handler: new QueueHandler(HttpStatusCode.OK, """{"data":[{"id":"gemma3"}]}"""));
        Assert.False(result.Ok);
        Assert.Empty(result.Models);
        Assert.Contains("沒有支援工具呼叫的模型", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_Unauthorized_IsFriendly()
    {
        var handler = new QueueHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid"}}""");
        var result = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("https://api.openai.com/v1", "gpt-4o-mini", "bad"),
            handler);
        Assert.False(result.Ok);
        Assert.Contains("API key", result.Message);
    }

    [Fact]
    public async Task ProbeAsync_FallsBackToChat_WhenModelsMissing()
    {
        var handler = new QueueHandler(
            (HttpStatusCode.NotFound, """{"error":"no"}"""),
            (HttpStatusCode.OK, """{"choices":[{"message":{"role":"assistant","content":"."}}]}"""));
        var result = await ProjectAskService.ProbeAsync(
            new ProjectAskOptions("http://127.0.0.1:9/v1", "test-model"),
            handler);
        Assert.True(result.Ok);
        Assert.True(result.ModelFound);
        Assert.Contains("沒有模型清單", result.Message);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("ping", handler.Requests[1]);
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

    [Fact]
    public async Task AskAsync_RejectsGemmaBeforeHttp()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var handler = new QueueHandler("should-not-be-called");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProjectAskService.AskAsync(
                    ws,
                    "目前MCP有哪些服務",
                    [],
                    new ProjectAskOptions("http://127.0.0.1:9/v1", "gemma3:4b"),
                    handler: handler));
            Assert.Contains("不支援工具呼叫", ex.Message);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AskAsync_MapsToolsUnsupportedHttpError()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var handler = new QueueHandler(
                HttpStatusCode.BadRequest,
                """{"error":{"message":"mystery-7b does not support tools","type":"invalid_request_error"}}""");
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ProjectAskService.AskAsync(
                    ws,
                    "現在能不能交班？",
                    [],
                    new ProjectAskOptions("http://127.0.0.1:9/v1", "mystery-7b"),
                    handler: handler));
            Assert.Contains("不支援工具呼叫", ex.Message);
            Assert.DoesNotContain("HTTP 400", ex.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PullAsync_StreamsProgress_ThenSucceeds()
    {
        var statuses = new List<string>();
        var handler = new QueueHandler("""
            {"status":"pulling manifest"}
            {"status":"downloading","total":1000,"completed":400}
            {"status":"success"}
            """);
        var result = await ProjectAskService.PullAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "llama3.2"),
            onStatus: statuses.Add,
            handler: handler);
        Assert.True(result.Ok);
        Assert.Contains("已 pull llama3.2", result.Message);
        Assert.Contains(statuses, s => s.Contains("40%"));
        Assert.Contains("llama3.2", handler.Requests[0]);
        Assert.Contains("/api/pull", handler.Uris[0]);
    }

    [Fact]
    public async Task PullAsync_RejectsGemma()
    {
        var handler = new QueueHandler("should-not-be-called");
        var result = await ProjectAskService.PullAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "gemma3:4b"),
            handler: handler);
        Assert.False(result.Ok);
        Assert.Contains("不支援工具呼叫", result.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PullAsync_MapsStreamError()
    {
        var result = await ProjectAskService.PullAsync(
            new ProjectAskOptions("http://127.0.0.1:11434/v1", "llama3.2"),
            handler: new QueueHandler("""{"error":"pull model manifest: file does not exist"}"""));
        Assert.False(result.Ok);
        Assert.Contains("file does not exist", result.Message);
    }

    sealed class QueueHandler : HttpMessageHandler
    {
        readonly Queue<(HttpStatusCode Status, string Body)> _replies;
        public List<string> Requests { get; } = [];
        public List<string> Uris { get; } = [];

        public QueueHandler(params string[] replies)
            : this(replies.Select(r => (HttpStatusCode.OK, r)).ToArray())
        {
        }

        public QueueHandler(HttpStatusCode status, string body)
            : this((status, body))
        {
        }

        public QueueHandler(params (HttpStatusCode Status, string Body)[] replies) =>
            _replies = new Queue<(HttpStatusCode, string)>(replies);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Uris.Add(request.RequestUri?.ToString() ?? "");
            Requests.Add(string.IsNullOrEmpty(body) ? request.RequestUri?.ToString() ?? "" : body);
            var (status, reply) = _replies.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(reply, Encoding.UTF8, "application/json"),
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
