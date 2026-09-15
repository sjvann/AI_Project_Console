using System.Net;
using AiProject.Analysis.Application;
using AiProject.Analysis.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiProject.Analysis.Web.Tests;

public class AnalysisWebTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> _factory;

    public AnalysisWebTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_ok()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("analysis", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Home_renders()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("系統分析輔助", html);
        Assert.Contains("開一則分析案", html);
    }

    [Fact]
    public async Task Artifact_and_issue_endpoints()
    {
        var store = new MemoryAnalysisCaseStore();
        var seeded = AnalysisCase.Start("API案", "痛點文字", DateTimeOffset.UtcNow, ["假設可見"]);
        seeded.ProduceRequirements(DateTimeOffset.UtcNow);
        seeded.ProduceSpec(DateTimeOffset.UtcNow);
        seeded.PrepareIssues("acme/demo", DateTimeOffset.UtcNow);
        seeded.MarkIssuesPublished(
        [
            new PublishedIssue
            {
                LocalId = "ISSUE-001",
                Number = 42,
                HtmlUrl = "https://github.com/acme/demo/issues/42#dry-run",
                Assignee = "alice",
                DryRun = true,
            },
        ], DateTimeOffset.UtcNow);
        await store.AddAsync(seeded);

        var opts = Options.Create(new AnalysisGitHubOptions { Repo = "acme/demo", DryRun = true });
        var cmds = new AnalysisCaseCommands(store, new DryRunIssuePublisher("acme/demo"), opts);

        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAnalysisCaseStore>(store);
                services.AddSingleton<IAnalysisIssuePublisher>(new DryRunIssuePublisher("acme/demo"));
                services.AddSingleton(cmds);
            });
        });

        var client = factory.CreateClient();
        var md = await client.GetStringAsync($"/api/cases/{seeded.Id}/requirements.md");
        Assert.Contains("# 需求文件", md);
        Assert.Contains("假設可見", md);

        var issuesMd = await client.GetStringAsync($"/api/cases/{seeded.Id}/issues.md");
        Assert.Contains("# Issue 包", issuesMd);
        Assert.Contains("ISSUE-001", issuesMd);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/cases/{seeded.Id}/issues.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/cases/{seeded.Id}/spec.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/cases/{Guid.NewGuid()}/requirements.md")).StatusCode);
    }
}
