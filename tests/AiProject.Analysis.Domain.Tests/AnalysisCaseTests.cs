using AiProject.Analysis.Domain;

namespace AiProject.Analysis.Domain.Tests;

public class AnalysisCaseTests
{
    [Fact]
    public void Start_requires_title_and_concept()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<AnalysisException>(() => AnalysisCase.Start("", "x", now));
        Assert.Throws<AnalysisException>(() => AnalysisCase.Start("t", "", now));
    }

    [Fact]
    public void Start_always_has_visible_assumption_prd_sa06()
    {
        var c = AnalysisCase.Start("庫存", "門市查詢太慢", DateTimeOffset.UtcNow);
        Assert.Equal(AnalysisCaseStatus.Concept, c.Status);
        Assert.NotEmpty(c.Assumptions);
        Assert.Contains(c.Assumptions, a => a.Contains("尚未經窗口確認", StringComparison.Ordinal));
    }

    [Fact]
    public void Start_keeps_explicit_assumptions()
    {
        var c = AnalysisCase.Start("庫存", "痛點", DateTimeOffset.UtcNow, ["只做 Web"], ["要離線嗎"]);
        Assert.Equal(["只做 Web"], c.Assumptions);
        Assert.Equal(["要離線嗎"], c.OpenQuestions);
    }
}
