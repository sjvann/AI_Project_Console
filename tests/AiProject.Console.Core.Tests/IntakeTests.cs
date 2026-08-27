using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Intake;
using AiProject.Console.Core.WorkHours;

namespace AiProject.Console.Core.Tests;

public class IntakeTests
{
    [Fact]
    public void Gates_DesignChangeNeedsImpactAndDoc()
    {
        var intake = new IntakeRecord
        {
            Title = "改登入",
            Body = "說明",
            GithubSlug = "acme/app",
            Kind = IntakeKinds.DesignChange,
            Items = [new IntakeWorkItem { Title = "改 API", AcceptanceCriteria = ["可登入"] }],
        };
        Assert.Equal("設計變更要填現況與期望。", IntakeGates.BlockDesignReady(intake));
        intake.AsIs = "舊";
        intake.ToBe = "新";
        Assert.Equal("設計變更要填影響範圍。", IntakeGates.BlockDesignReady(intake));
        intake.Impact = "登入流";
        Assert.Equal("設計變更要掛上設計或架構頁。", IntakeGates.BlockDesignReady(intake));
        intake.DesignDocs = ["engineering/architecture.md"];
        Assert.Null(IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Gates_CannotPublishWithoutAcceptance()
    {
        var intake = ReadyRequirement();
        intake.Items[0].AcceptanceCriteria = [""];
        Assert.Contains("驗收條件", IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Lifecycle_DraftToSplit()
    {
        var intake = new IntakeRecord { Title = "A", Body = "B", GithubSlug = "acme/app" };
        Assert.Equal(IntakeStages.DesignReady, IntakeLifecycle.Derive(intake).Stage);
        intake.Items.Add(new IntakeWorkItem { Title = "做 A", AcceptanceCriteria = [""] });
        Assert.Equal(IntakeStages.Split, IntakeLifecycle.Derive(intake).Stage);
        intake.Items[0].AcceptanceCriteria = ["通過測試"];
        Assert.Equal(IntakeStages.Split, IntakeLifecycle.Derive(intake).Stage);
    }

    [Fact]
    public void Lifecycle_IssuedAndReviewFromTraces()
    {
        var intake = ReadyRequirement();
        intake.Items[0].IssueNumber = 9;
        var traces = new Dictionary<int, IssueTrace>
        {
            [9] = new(9, "做 A", "OPEN", "https://example/9", ["dev"], "https://example/pull/3", "OPEN", "ok", "檢查已過", true, false),
        };
        var applied = IntakeLifecycle.ApplyTraces(intake, traces);
        Assert.Equal(IntakeStages.Review, applied.Stage);
        Assert.Equal("https://example/pull/3", applied.Items[0].PrUrl);
    }

    [Fact]
    public void IssueBody_IncludesIntakeIdAndCriteria()
    {
        var intake = ReadyRequirement();
        intake.Id = "REQ-1";
        var body = IntakeLifecycle.IssueBody(intake, intake.Items[0]);
        Assert.Contains("intake: REQ-1", body);
        Assert.Contains("通過測試", body);
    }

    [Fact]
    public void Store_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "apc-intake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new IntakeDocument { Intakes = [ReadyRequirement()] };
            doc.Intakes[0].Id = "REQ-TEST";
            IntakeStore.Save(root, doc);
            var loaded = IntakeStore.Load(root);
            Assert.Equal("REQ-TEST", loaded.Intakes[0].Id);
            Assert.Equal("通過測試", loaded.Intakes[0].Items[0].AcceptanceCriteria[0]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Gates_AcceptNeedsChecksMergedAndDeploy()
    {
        var intake = ReadyRequirement();
        intake.Items[0].IssueNumber = 9;
        intake.Items[0].AcceptanceDone = [true];
        Assert.Contains("尚未部署", IntakeGates.BlockAccept(intake));
        intake.SkipDeploy = true;
        Assert.Null(IntakeGates.BlockAccept(intake));
        intake.Items[0].PrUrl = "https://example/pull/3";
        intake.Items[0].PrState = "OPEN";
        Assert.Contains("尚未入主線", IntakeGates.BlockAccept(intake));
        intake.Items[0].PrState = "MERGED";
        Assert.Null(IntakeGates.BlockAccept(intake));
    }

    [Fact]
    public void Gates_MergeNeedsGreenChecks()
    {
        var intake = ReadyRequirement();
        Assert.Contains("還沒有連結", IntakeGates.BlockMerge(intake));
        intake.Items[0].PrUrl = "https://example/pull/3";
        intake.Items[0].CiTone = "wait";
        Assert.Contains("還沒綠", IntakeGates.BlockMerge(intake));
        intake.Items[0].CiTone = "warn";
        Assert.Contains("未通過", IntakeGates.BlockMerge(intake));
        intake.Items[0].CiTone = "ok";
        Assert.Null(IntakeGates.BlockMerge(intake));
    }

    [Fact]
    public void PublishPreview_IncludesBody()
    {
        var intake = ReadyRequirement();
        intake.Id = "REQ-1";
        var preview = IntakeLifecycle.PublishPreview(intake);
        Assert.Contains("intake: REQ-1", preview);
        Assert.Contains("通過測試", preview);
    }

    [Fact]
    public void ParsePulls_FindsClosingIssueAndCi()
    {
        var json = """
            [{"number":3,"title":"fix #9","url":"https://example/pull/3","state":"OPEN","body":"Closes #9","statusCheckRollup":[{"conclusion":"SUCCESS"}],"mergedAt":""}]
            """;
        var pulls = GitHubLifecycle.ParsePulls(json);
        Assert.Single(pulls);
        Assert.Contains(9, pulls[0].Closes);
        Assert.True(pulls[0].ChecksGreen);
        Assert.Equal("ok", pulls[0].CiTone);
    }

    [Fact]
    public void ParseRuns_ReadsId()
    {
        var json = """[{"databaseId":77,"status":"completed","conclusion":"success","url":"https://example/77","displayTitle":"deploy"}]""";
        var run = ActionsStatus.ParseRuns(json).Single();
        Assert.Equal(77, run.DatabaseId);
        Assert.Equal("success", run.Conclusion);
    }

    [Fact]
    public void HoursInbox_MatchesIssueToIntake()
    {
        var csv = WorkHoursExport.ItemsHeader + "\n" +
                  "github:alice,alice,App,acme/app,issue,9,做 A,https://x,closed,2026-08-01\n";
        var items = WorkHoursInbox.ParseItemsCsv(csv);
        Assert.Single(items);
        Assert.Equal(9, items[0].Number);
        var intake = ReadyRequirement();
        intake.Id = "REQ-9";
        intake.Items[0].IssueNumber = 9;
        var match = WorkHoursInbox.Match(items, [intake]);
        Assert.Equal("REQ-9", match[0].IntakeId);
    }

    static IntakeRecord ReadyRequirement() =>
        new()
        {
            Title = "新功能",
            Body = "說明",
            GithubSlug = "acme/app",
            Kind = IntakeKinds.Requirement,
            Items =
            [
                new IntakeWorkItem
                {
                    Title = "做 A",
                    AcceptanceCriteria = ["通過測試"],
                    AcceptanceDone = [false],
                },
            ],
        };
}
