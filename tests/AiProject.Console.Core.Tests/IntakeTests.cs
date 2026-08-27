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
        Assert.Equal("設計變更要上傳分析或設計文件。", IntakeGates.BlockDesignReady(intake));
        intake.DesignDocs = ["docs/product/design/ECR-1/分析.md"];
        Assert.Null(IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Gates_UiRequirementNeedsSketch()
    {
        var intake = ReadyRequirement();
        intake.IsUi = true;
        Assert.Equal("介面新需求要附一張草圖。", IntakeGates.BlockDesignReady(intake));
        intake.Sketches = [new IntakeVisual { Path = "docs/product/intake-assets/REQ-1/sketch-1.png" }];
        Assert.Null(IntakeGates.BlockDesignReady(intake));
    }

    [Fact]
    public void Gates_UiDesignChangeNeedsCropNote()
    {
        var intake = new IntakeRecord
        {
            Title = "改登入鈕",
            Body = "說明",
            GithubSlug = "acme/app",
            Kind = IntakeKinds.DesignChange,
            IsUi = true,
            AsIs = "舊",
            ToBe = "新",
            Impact = "登入",
            DesignDocs = ["docs/product/design/ECR-1/分析.md"],
            Items = [new IntakeWorkItem { Title = "改鈕", AcceptanceCriteria = ["對得上剪圖"] }],
        };
        Assert.Equal("介面設計變更要提供剪圖，標出修改處。", IntakeGates.BlockDesignReady(intake));
        intake.Crops = [new IntakeVisual { Path = "docs/product/intake-assets/ECR-1/crop-1.png" }];
        Assert.Equal("每張剪圖都要寫修改說明。", IntakeGates.BlockDesignReady(intake));
        intake.Crops[0].Note = "把登入改成主色";
        Assert.Null(IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Gates_DeletePauseRecallRules()
    {
        var draft = ReadyRequirement();
        Assert.True(IntakeGates.CanDelete(draft));
        Assert.False(IntakeGates.CanPause(draft));
        Assert.False(IntakeGates.CanRecall(draft));
        draft.Items[0].IssueNumber = 9;
        Assert.False(IntakeGates.CanDelete(draft));
        Assert.True(IntakeGates.CanPause(draft));
        Assert.True(IntakeGates.CanRecall(draft));
        draft.Hold = IntakeHolds.Paused;
        Assert.False(IntakeGates.CanPause(draft));
        Assert.True(IntakeGates.CanResume(draft));
        Assert.True(IntakeGates.CanRecall(draft));
        draft.Hold = IntakeHolds.Recalled;
        Assert.False(IntakeGates.CanRecall(draft));
        Assert.False(IntakeGates.CanResume(draft));
        Assert.Equal("此進件已收回。", IntakeGates.BlockWhileHeld(draft));
    }

    [Fact]
    public void Lifecycle_HoldOverlaysBlockReason()
    {
        var intake = ReadyRequirement();
        intake.Items[0].IssueNumber = 9;
        intake.Hold = IntakeHolds.Paused;
        var paused = IntakeLifecycle.Derive(intake);
        Assert.Equal(IntakeStages.Issued, paused.Stage);
        Assert.Contains("已暫停執行", paused.Block);
        intake.Hold = IntakeHolds.Recalled;
        var recalled = IntakeLifecycle.Derive(intake);
        Assert.Contains("已收回", recalled.Block);
    }

    [Fact]
    public void IssueBody_IncludesUiVisuals()
    {
        var req = ReadyRequirement();
        req.Id = "REQ-1";
        req.IsUi = true;
        req.Sketches = [new IntakeVisual { Path = "docs/product/intake-assets/REQ-1/sketch-1.png", Note = "首頁線框" }];
        var reqBody = IntakeLifecycle.IssueBody(req, req.Items[0]);
        Assert.Contains("介面草圖", reqBody);
        Assert.Contains("sketch-1.png", reqBody);
        var ecr = ReadyRequirement();
        ecr.Kind = IntakeKinds.DesignChange;
        ecr.IsUi = true;
        ecr.AsIs = "舊";
        ecr.ToBe = "新";
        ecr.Impact = "登入";
        ecr.Crops = [new IntakeVisual { Path = "docs/product/intake-assets/ECR-1/crop-1.png", Note = "改按鈕" }];
        var ecrBody = IntakeLifecycle.IssueBody(ecr, ecr.Items[0]);
        Assert.Contains("現況剪圖", ecrBody);
        Assert.Contains("改按鈕", ecrBody);
    }

    [Fact]
    public void Assets_RejectsTraversalAndRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "apc-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(IntakeAssets.TryResolve(root, "../secret.png", out _));
            Assert.False(IntakeAssets.TryResolve(root, "docs/user/help.md", out _));
            var src = Path.Combine(root, "src.png");
            File.WriteAllBytes(src, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var rel = IntakeAssets.CopyIn(root, "REQ-1", src, "sketch");
            Assert.StartsWith("docs/product/intake-assets/REQ-1/", rel);
            Assert.True(IntakeAssets.TryResolve(root, rel, out var full));
            Assert.True(File.Exists(full));
            Assert.StartsWith("data:image/png;base64,", IntakeAssets.TryDataUrl(root, rel));
            IntakeAssets.DeleteFolder(root, "REQ-1");
            Assert.False(File.Exists(full));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Store_RoundTripUiAndHold()
    {
        var root = Path.Combine(Path.GetTempPath(), "apc-intake-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new IntakeDocument { Intakes = [ReadyRequirement()] };
            doc.Intakes[0].Id = "REQ-UI";
            doc.Intakes[0].IsUi = true;
            doc.Intakes[0].Hold = IntakeHolds.Paused;
            doc.Intakes[0].Sketches = [new IntakeVisual { Path = "docs/product/intake-assets/REQ-UI/a.png", Note = "線框" }];
            IntakeStore.Save(root, doc);
            var loaded = IntakeStore.Load(root);
            Assert.True(loaded.Intakes[0].IsUi);
            Assert.Equal(IntakeHolds.Paused, loaded.Intakes[0].Hold);
            Assert.Equal("線框", loaded.Intakes[0].Sketches[0].Note);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void DesignFiles_StayUnderDocsAndKeepName()
    {
        Assert.Equal("docs/product/design", IntakeDesignFiles.NormalizeDir(""));
        Assert.Equal("docs/engineering/ecr", IntakeDesignFiles.NormalizeDir("docs/engineering/ecr"));
        Assert.Throws<InvalidOperationException>(() => IntakeDesignFiles.NormalizeDir("../secret"));
        Assert.Throws<InvalidOperationException>(() => IntakeDesignFiles.NormalizeDir("src/design"));
        var root = Path.Combine(Path.GetTempPath(), "apc-designdoc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var src = Path.Combine(root, "登入分析.md");
            File.WriteAllText(src, "# 分析");
            var rel = IntakeDesignFiles.CopyIn(root, "docs/product/design", "ECR-1", src);
            Assert.Equal("docs/product/design/ECR-1/登入分析.md", rel);
            Assert.True(IntakeDesignFiles.TryResolve(root, rel, out var full));
            Assert.True(File.Exists(full));
            Assert.False(IntakeDesignFiles.TryResolve(root, "src/secret.md", out _));
            Assert.Contains("/blob/main/docs/product/design/ECR-1/", IntakeDesignFiles.BlobUrl("https://github.com/acme/app", "main", rel));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IssueBody_UsesRemoteLinksWhenPublishing()
    {
        var intake = ReadyRequirement();
        intake.Id = "REQ-1";
        intake.DesignDocs = ["docs/product/design/REQ-1/spec.md"];
        var body = IntakeLifecycle.IssueBody(intake, intake.Items[0], new IntakeIssueLinks("https://github.com/acme/app", "main"));
        Assert.Contains("分析／設計文件", body);
        Assert.Contains("[spec.md](https://github.com/acme/app/blob/main/docs/product/design/REQ-1/spec.md)", body);
        var paths = IntakeLifecycle.PublishRelPaths(intake);
        Assert.Contains(IntakeStore.RelPath, paths);
        Assert.Contains("docs/product/design/REQ-1/spec.md", paths);
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
