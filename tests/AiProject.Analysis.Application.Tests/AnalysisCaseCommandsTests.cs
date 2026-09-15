using AiProject.Analysis.Application;
using AiProject.Analysis.Domain;
using Microsoft.Extensions.Options;

namespace AiProject.Analysis.Application.Tests;

public class AnalysisCaseCommandsTests
{
    static AnalysisCaseCommands Create(IAnalysisCaseStore? store = null, string repo = "acme/demo")
    {
        store ??= new MemoryAnalysisCaseStore();
        var opts = Options.Create(new AnalysisGitHubOptions { Repo = repo, DryRun = true });
        return new AnalysisCaseCommands(store, new DryRunIssuePublisher(repo), opts);
    }

    [Fact]
    public async Task StartAsync_persists_and_lists()
    {
        var cmds = Create();
        var created = await cmds.StartAsync("門市庫存", "查詢太慢", ["只做 Web"], ["要離線嗎"]);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal(AnalysisCaseStatus.Concept, created.Status);

        var got = await cmds.GetAsync(created.Id);
        Assert.NotNull(got);
        Assert.Equal("門市庫存", got!.Title);

        var list = await cmds.ListAsync();
        Assert.Contains(list, x => x.Id == created.Id);
    }

    [Fact]
    public async Task StartAsync_rejects_blank()
    {
        var cmds = Create();
        await Assert.ThrowsAsync<AnalysisException>(() => cmds.StartAsync("", "x"));
        await Assert.ThrowsAsync<AnalysisException>(() => cmds.StartAsync("t", "  "));
    }

    [Fact]
    public async Task Produce_requirements_then_spec()
    {
        var cmds = Create();
        var created = await cmds.StartAsync("庫存", "查詢太慢", ["假設A"], ["問題B"]);
        var req = await cmds.ProduceRequirementsAsync(created.Id, ["窗口"], ["範圍內"], ["範圍外"], ["驗收"]);
        Assert.Contains(req.Assumptions, a => a == "假設A");
        Assert.Equal(AnalysisCaseStatus.Requirements, (await cmds.GetAsync(created.Id))!.Status);

        var spec = await cmds.ProduceSpecAsync(created.Id);
        Assert.Contains(spec.Items, i => i.TracesRequirementIds.Contains("REQ-001"));
        Assert.Equal(AnalysisCaseStatus.Spec, (await cmds.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task Publish_issues_dry_run_assigns_feature()
    {
        var cmds = Create();
        var created = await cmds.StartAsync("庫存", "查詢太慢", ["假設A"], ["問題B"]);
        await cmds.ProduceRequirementsAsync(created.Id);
        await cmds.ProduceSpecAsync(created.Id);
        var pack = await cmds.PublishIssuesAsync(created.Id, assignee: "alice");
        Assert.Equal(AnalysisCaseStatus.Issues, (await cmds.GetAsync(created.Id))!.Status);
        Assert.NotEmpty(pack.Published);
        Assert.All(pack.Published, p => Assert.True(p.DryRun));
        Assert.Contains(pack.Published, p => p.Assignee == "alice");
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Test);
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Acceptance);
        Assert.Contains("假設", pack.ToMarkdown());
    }
}
