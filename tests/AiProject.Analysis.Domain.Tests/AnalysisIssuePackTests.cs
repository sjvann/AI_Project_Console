using AiProject.Analysis.Domain;

namespace AiProject.Analysis.Domain.Tests;

public class AnalysisIssuePackTests
{
    [Fact]
    public void PrepareIssues_requires_spec_and_repo()
    {
        var c = AnalysisCase.Start("庫存", "痛點", DateTimeOffset.UtcNow);
        Assert.Throws<AnalysisException>(() => c.PrepareIssues("acme/demo", DateTimeOffset.UtcNow));
        c.ProduceRequirements(DateTimeOffset.UtcNow);
        c.ProduceSpec(DateTimeOffset.UtcNow);
        Assert.Throws<AnalysisException>(() => c.PrepareIssues("", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PrepareIssues_includes_feature_test_acceptance_and_traces()
    {
        var c = AnalysisCase.Start("庫存", "門市查詢太慢", DateTimeOffset.UtcNow, ["只做 Web"], ["要離線嗎"]);
        c.ProduceRequirements(DateTimeOffset.UtcNow);
        c.ProduceSpec(DateTimeOffset.UtcNow);
        var pack = c.PrepareIssues("acme/demo", DateTimeOffset.UtcNow);
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Feature);
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Test);
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Acceptance);
        Assert.Contains(pack.Drafts, d => d.Kind == IssueKind.Debug);
        Assert.Contains(pack.Drafts, d => d.Labels.Contains("needs-assignment"));
        Assert.All(pack.Drafts.Where(d => d.Kind == IssueKind.Feature), d => Assert.NotEmpty(d.TracesRequirementIds));
        Assert.Contains("假設", pack.ToMarkdown());
    }

    [Fact]
    public void MarkPublished_moves_status_to_issues()
    {
        var c = AnalysisCase.Start("庫存", "痛點", DateTimeOffset.UtcNow);
        c.ProduceRequirements(DateTimeOffset.UtcNow);
        c.ProduceSpec(DateTimeOffset.UtcNow);
        c.PrepareIssues("acme/demo", DateTimeOffset.UtcNow);
        c.MarkIssuesPublished(
        [
            new PublishedIssue { LocalId = "ISSUE-001", Number = 1, HtmlUrl = "https://example/1", Assignee = "bob", DryRun = true },
        ], DateTimeOffset.UtcNow);
        Assert.Equal(AnalysisCaseStatus.Issues, c.Status);
        Assert.Single(c.Issues!.Published);
    }
}
