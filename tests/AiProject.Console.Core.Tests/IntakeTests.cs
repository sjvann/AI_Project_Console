using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Intake;
using AiProject.Console.Core.WorkHours;

namespace AiProject.Console.Core.Tests;

public class IntakeTests
{
    [Fact]
    public void Gates_PublishNeedsTitleBodyAndSlug()
    {
        var intake = new IntakeRecord();
        Assert.Equal("請先填標題。", IntakeGates.BlockPublish(intake));
        intake.Title = "登入失敗";
        Assert.Equal("請先寫說明。", IntakeGates.BlockPublish(intake));
        intake.Body = "UAT 按登入沒反應。";
        Assert.Equal("請指定目標倉（owner/repo）。", IntakeGates.BlockPublish(intake));
        intake.GithubSlug = "acme/app";
        Assert.Null(IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Gates_CanPublishWithoutAcceptanceDesignOrKind()
    {
        var intake = ReadyIssue();
        intake.Kind = IntakeKinds.DesignChange;
        Assert.Null(IntakeGates.BlockPublish(intake));
        intake.IsUi = true;
        Assert.Null(IntakeGates.BlockPublish(intake));
    }

    [Fact]
    public void Gates_DeletePauseRecallRules()
    {
        var draft = ReadyIssue();
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
        var intake = ReadyIssue();
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
    public void IssueBody_IncludesAttachmentsAndLegacyFields()
    {
        var issue = ReadyIssue();
        issue.Id = "ISS-1";
        issue.Sketches = [new IntakeVisual { Path = "docs/product/intake-assets/ISS-1/img-1.png", Note = "首頁" }];
        var body = IntakeLifecycle.IssueBody(issue, issue.Items[0]);
        Assert.Contains("## 附件", body);
        Assert.Contains("img-1.png", body);
        Assert.Contains("首頁", body);
        Assert.DoesNotContain("## 驗收條件", body);
        var ecr = ReadyIssue();
        ecr.Kind = IntakeKinds.DesignChange;
        ecr.AsIs = "舊";
        ecr.ToBe = "新";
        ecr.Impact = "登入";
        ecr.Crops = [new IntakeVisual { Path = "docs/product/intake-assets/ECR-1/crop-1.png", Note = "改按鈕" }];
        var ecrBody = IntakeLifecycle.IssueBody(ecr, ecr.Items[0]);
        Assert.Contains("## 設計變更", ecrBody);
        Assert.Contains("改按鈕", ecrBody);
        Assert.Contains("## 附件", ecrBody);
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
            var rel = IntakeAssets.CopyIn(root, "ISS-1", src);
            Assert.StartsWith("docs/product/intake-assets/ISS-1/", rel);
            Assert.Contains("/img-", rel.Replace('\\', '/'));
            Assert.True(IntakeAssets.TryResolve(root, rel, out var full));
            Assert.True(File.Exists(full));
            Assert.StartsWith("data:image/png;base64,", IntakeAssets.TryDataUrl(root, rel));
            IntakeAssets.DeleteFolder(root, "ISS-1");
            Assert.False(File.Exists(full));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Store_RoundTripLegacyKindAndHold()
    {
        var root = Path.Combine(Path.GetTempPath(), "apc-intake-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new IntakeDocument { Intakes = [ReadyIssue()] };
            doc.Intakes[0].Id = "REQ-UI";
            doc.Intakes[0].Kind = IntakeKinds.Requirement;
            doc.Intakes[0].IsUi = true;
            doc.Intakes[0].Hold = IntakeHolds.Paused;
            doc.Intakes[0].AsIs = "舊畫面";
            doc.Intakes[0].Sketches = [new IntakeVisual { Path = "docs/product/intake-assets/REQ-UI/a.png", Note = "線框" }];
            IntakeStore.Save(root, doc);
            var loaded = IntakeStore.Load(root);
            Assert.Equal(IntakeKinds.Requirement, loaded.Intakes[0].Kind);
            Assert.True(loaded.Intakes[0].IsUi);
            Assert.Equal(IntakeHolds.Paused, loaded.Intakes[0].Hold);
            Assert.Equal("舊畫面", loaded.Intakes[0].AsIs);
            Assert.Equal("線框", loaded.Intakes[0].Sketches[0].Note);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Store_NewRecordUsesIssuePrefix()
    {
        var record = IntakeStore.NewRecord("alice");
        Assert.StartsWith("ISS-", record.Id);
        Assert.Equal(IntakeKinds.Issue, record.Kind);
        Assert.Equal("alice", record.Requester);
        Assert.Single(record.Items);
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
        var intake = ReadyIssue();
        intake.Id = "ISS-1";
        intake.DesignDocs = ["docs/product/design/ISS-1/spec.md"];
        var body = IntakeLifecycle.IssueBody(intake, intake.Items[0], new IntakeIssueLinks("https://github.com/acme/app", "main"));
        Assert.Contains("分析／設計文件", body);
        Assert.Contains("[spec.md](https://github.com/acme/app/blob/main/docs/product/design/ISS-1/spec.md)", body);
        var paths = IntakeLifecycle.PublishRelPaths(intake);
        Assert.Contains(IntakeStore.RelPath, paths);
        Assert.Contains("docs/product/design/ISS-1/spec.md", paths);
    }

    [Fact]
    public void Lifecycle_DraftUntilIssued()
    {
        var intake = new IntakeRecord { Title = "A", Body = "B", GithubSlug = "acme/app" };
        Assert.Equal(IntakeStages.Draft, IntakeLifecycle.Derive(intake).Stage);
        Assert.Equal("", IntakeLifecycle.Derive(intake).Block);
        intake.Items.Add(new IntakeWorkItem { Title = "做 A" });
        Assert.Equal(IntakeStages.Draft, IntakeLifecycle.Derive(intake).Stage);
        intake.Items[0].IssueNumber = 9;
        Assert.Equal(IntakeStages.Issued, IntakeLifecycle.Derive(intake).Stage);
    }

    [Fact]
    public void Lifecycle_MapsLegacyStagesToRail()
    {
        Assert.Equal(IntakeStages.Draft, IntakeStages.Canonical(IntakeStages.DesignReady));
        Assert.Equal(IntakeStages.Draft, IntakeStages.Canonical(IntakeStages.Split));
        Assert.Equal(IntakeStages.Doing, IntakeStages.Canonical(IntakeStages.Review));
        Assert.Equal(IntakeStages.Doing, IntakeStages.Canonical(IntakeStages.Merged));
        Assert.Equal(IntakeStages.Accepted, IntakeStages.Canonical(IntakeStages.Billed));
        Assert.Equal(4, IntakeStages.All.Count);
        Assert.Equal(1, IntakeStages.IndexOf(IntakeStages.Issued));
        Assert.Equal(2, IntakeStages.IndexOf(IntakeStages.Review));
    }

    [Fact]
    public void Lifecycle_IssuedAndDoingFromTraces()
    {
        var intake = ReadyIssue();
        intake.Items[0].IssueNumber = 9;
        var traces = new Dictionary<int, IssueTrace>
        {
            [9] = new(9, "做 A", "OPEN", "https://example/9", ["dev"], "https://example/pull/3", "OPEN", "ok", "檢查已過", true, false),
        };
        var applied = IntakeLifecycle.ApplyTraces(intake, traces);
        Assert.Equal(IntakeStages.Doing, applied.Stage);
        Assert.Equal("https://example/pull/3", applied.Items[0].PrUrl);
    }

    [Fact]
    public void IssueBody_IncludesIntakeIdWithoutForcedCriteria()
    {
        var intake = ReadyIssue();
        intake.Id = "ISS-1";
        var body = IntakeLifecycle.IssueBody(intake, intake.Items[0]);
        Assert.Contains("intake: ISS-1", body);
        Assert.Contains("UAT 按登入沒反應。", body);
        Assert.DoesNotContain("## 驗收條件", body);
    }

    [Fact]
    public void Store_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), "apc-intake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = new IntakeDocument { Intakes = [ReadyIssue()] };
            doc.Intakes[0].Id = "ISS-TEST";
            IntakeStore.Save(root, doc);
            var loaded = IntakeStore.Load(root);
            Assert.Equal("ISS-TEST", loaded.Intakes[0].Id);
            Assert.Equal(IntakeKinds.Issue, loaded.Intakes[0].Kind);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Gates_AcceptNeedsIssueOnly()
    {
        var intake = ReadyIssue();
        Assert.Equal("尚未發出 Issue。", IntakeGates.BlockAccept(intake));
        intake.Items[0].IssueNumber = 9;
        Assert.Null(IntakeGates.BlockAccept(intake));
        intake.Items[0].PrUrl = "https://example/pull/3";
        intake.Items[0].PrState = "OPEN";
        Assert.Null(IntakeGates.BlockAccept(intake));
    }

    [Fact]
    public void PublishPreview_IncludesBody()
    {
        var intake = ReadyIssue();
        intake.Id = "ISS-1";
        var preview = IntakeLifecycle.PublishPreview(intake);
        Assert.Contains("intake: ISS-1", preview);
        Assert.Contains("UAT 按登入沒反應。", preview);
        Assert.DoesNotContain("## 驗收條件", preview);
    }

    [Fact]
    public void MergeAttachments_MovesCropsIntoSketches()
    {
        var intake = ReadyIssue();
        intake.Crops = [new IntakeVisual { Path = "docs/product/intake-assets/ECR-1/crop-1.png", Note = "改鈕" }];
        intake.MergeAttachments();
        Assert.Empty(intake.Crops);
        Assert.Contains(intake.Sketches, s => s.Path.Contains("crop-1.png", StringComparison.Ordinal));
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
        var intake = ReadyIssue();
        intake.Id = "ISS-9";
        intake.Items[0].IssueNumber = 9;
        var match = WorkHoursInbox.Match(items, [intake]);
        Assert.Equal("ISS-9", match[0].IntakeId);
    }

    [Fact]
    public void Counts_IssuedAndPending()
    {
        var draft = ReadyIssue();
        Assert.False(draft.CountsAsIssued);
        var open = ReadyIssue();
        open.Items[0].IssueNumber = 9;
        Assert.True(open.CountsAsIssued);
        Assert.True(open.CountsAsPendingAcceptance);
        open.AcceptedAt = "2026-09-23T00:00:00Z";
        Assert.True(open.CountsAsIssued);
        Assert.False(open.CountsAsPendingAcceptance);
        open.Hold = IntakeHolds.Recalled;
        open.AcceptedAt = null;
        Assert.False(open.CountsAsIssued);
    }

    static IntakeRecord ReadyIssue() =>
        new()
        {
            Title = "登入失敗",
            Body = "UAT 按登入沒反應。",
            GithubSlug = "acme/app",
            Kind = IntakeKinds.Issue,
            Items =
            [
                new IntakeWorkItem
                {
                    Title = "登入失敗",
                },
            ],
        };
}
