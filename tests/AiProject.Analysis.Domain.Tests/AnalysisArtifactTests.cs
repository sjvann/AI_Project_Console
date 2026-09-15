using AiProject.Analysis.Domain;

namespace AiProject.Analysis.Domain.Tests;

public class AnalysisArtifactTests
{
    [Fact]
    public void ProduceRequirements_embeds_assumptions_and_scope()
    {
        var c = AnalysisCase.Start("庫存", "門市查詢太慢", DateTimeOffset.UtcNow, ["只做 Web"], ["要離線嗎"]);
        var doc = c.ProduceRequirements(DateTimeOffset.UtcNow, ["窗口"], ["查詢加速"], ["不做 App"], ["P95 < 2s"]);
        Assert.Equal(AnalysisCaseStatus.Requirements, c.Status);
        Assert.Contains(doc.Assumptions, a => a.Contains("只做 Web"));
        Assert.Contains(doc.OpenQuestions, q => q.Contains("離線"));
        Assert.Contains(doc.InScope, x => x.Contains("查詢加速"));
        Assert.Contains(doc.OutOfScope, x => x.Contains("不做 App"));
        Assert.NotEmpty(doc.Requirements);
        Assert.Contains("## 假設", doc.ToMarkdown());
        Assert.Contains("REQ-001", doc.ToMarkdown());
        var json = doc.ToJsonShape();
        Assert.NotNull(json);
    }

    [Fact]
    public void ProduceSpec_traces_requirement_ids()
    {
        var c = AnalysisCase.Start("庫存", "痛點", DateTimeOffset.UtcNow);
        c.ProduceRequirements(DateTimeOffset.UtcNow);
        var spec = c.ProduceSpec(DateTimeOffset.UtcNow);
        Assert.Equal(AnalysisCaseStatus.Spec, c.Status);
        Assert.All(spec.Items, item => Assert.NotEmpty(item.TracesRequirementIds));
        Assert.Contains("SPEC-001", spec.ToMarkdown());
        Assert.Contains("REQ-001", spec.ToMarkdown());
        Assert.Contains("假設", spec.ToMarkdown());
    }

    [Fact]
    public void ProduceSpec_requires_requirements_first()
    {
        var c = AnalysisCase.Start("庫存", "痛點", DateTimeOffset.UtcNow);
        Assert.Throws<AnalysisException>(() => c.ProduceSpec(DateTimeOffset.UtcNow));
    }
}
