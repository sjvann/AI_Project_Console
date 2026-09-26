using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Intake;
using AiProject.Console.Core.Util;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    public IntakeDocument IntakeDoc { get; private set; } = new();
    public string? SelectedIntakeId { get; private set; }
    public string IntakeHint { get; private set; } = "";
    public bool IntakeBusy { get; private set; }
    public IReadOnlyList<string> Collaborators { get; private set; } = [];
    public IReadOnlyList<GithubIssue> GovernanceIssues { get; private set; } = [];
    public string HoldNoteDraft { get; set; } = "";

    public IntakeRecord? SelectedIntake =>
        IntakeStore.Find(IntakeDoc, SelectedIntakeId);

    public IReadOnlyList<IntakeRecord> VisibleIntakes => IntakeDoc.Intakes;

    public async Task RefreshIntakeAsync()
    {
        if (Catalog is null)
        {
            IntakeDoc = new();
            SelectedIntakeId = null;
            IntakeHint = "請先選擇專案或薄工作區。Issue 表寫在 docs/product/intake.json。";
            Notify();
            return;
        }
        IntakeDoc = IntakeStore.Load(Catalog.Root);
        if (string.IsNullOrWhiteSpace(IntakeDoc.DesignDocsDir))
            IntakeDoc.DesignDocsDir = IntakeDesignFiles.DefaultDir;
        foreach (var intake in IntakeDoc.Intakes)
            intake.MergeAttachments();
        if (SelectedIntake is null)
            SelectedIntakeId = IntakeDoc.Intakes.FirstOrDefault()?.Id;
        DeriveLocalStages();
        Notify();
        await RefreshIntakeRemoteAsync().ConfigureAwait(false);
    }

    public void SelectIntake(string? id)
    {
        SelectedIntakeId = id;
        HoldNoteDraft = SelectedIntake?.HoldNote ?? "";
        Notify();
    }

    public void AddIntake()
    {
        if (Catalog is null)
        {
            _native.Warn("需求工作台", "請先選擇專案目錄。");
            return;
        }
        var record = IntakeStore.NewRecord(GithubAccount.Login);
        var cfg = GithubDraft;
        if (!string.IsNullOrEmpty(cfg.Slug()))
        {
            record.GithubSlug = cfg.Slug();
            record.Host = cfg.ResolvedHost();
            record.EnsurePrimaryItem().GithubSlug = record.GithubSlug;
        }
        IntakeDoc.Intakes.Insert(0, record);
        SelectedIntakeId = record.Id;
        HoldNoteDraft = "";
        PersistIntake();
    }

    public void DeleteIntake(string id)
    {
        var intake = IntakeStore.Find(IntakeDoc, id);
        if (intake is null)
            return;
        if (!IntakeGates.CanDelete(intake))
        {
            _native.Warn("不能刪除", "已發出的進件請用「通知收回」或「暫停執行」，不要直接刪，以免遠端任務失聯。");
            return;
        }
        var label = string.IsNullOrWhiteSpace(intake.Title) ? intake.Id : intake.Id + " " + intake.Title;
        if (!_native.Confirm("刪除進件", $"確定刪除尚未發出的「{label}」？附件會一併移除。"))
            return;
        IntakeDoc.Intakes.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (Catalog is not null)
        {
            IntakeAssets.DeleteFolder(Catalog.Root, id);
            IntakeDesignFiles.DeleteIntakeFolder(Catalog.Root, IntakeDoc.DesignDocsDir, id);
        }
        if (string.Equals(SelectedIntakeId, id, StringComparison.OrdinalIgnoreCase))
            SelectedIntakeId = VisibleIntakes.FirstOrDefault()?.Id;
        PersistIntake();
    }

    public void PersistIntake()
    {
        if (Catalog is null)
            return;
        try
        {
            IntakeDoc.DesignDocsDir = IntakeDesignFiles.NormalizeDir(IntakeDoc.DesignDocsDir);
        }
        catch (InvalidOperationException)
        {
            IntakeDoc.DesignDocsDir = IntakeDesignFiles.DefaultDir;
        }
        foreach (var intake in IntakeDoc.Intakes)
        {
            FillIntakeSlug(intake);
            intake.MergeAttachments();
            intake.EnsurePrimaryItem();
        }
        DeriveLocalStages();
        IntakeStore.Save(Catalog.Root, IntakeDoc);
        Notify();
    }

    public string? IntakeVisualSrc(string rel) =>
        Catalog is null ? null : IntakeAssets.TryDataUrl(Catalog.Root, rel);

    public async Task AddIntakeVisualAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        intake.MergeAttachments();
        if (intake.Sketches.Count >= IntakeAssets.MaxFilesPerKind)
        {
            _native.Warn("附件已滿", $"同一進件最多 {IntakeAssets.MaxFilesPerKind} 張。");
            return;
        }
        var files = await _native.PickFilesAsync("選擇截圖", ("圖片", [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"])).ConfigureAwait(false);
        if (files is null || files.Length == 0)
            return;
        try
        {
            foreach (var file in files)
            {
                if (intake.Sketches.Count >= IntakeAssets.MaxFilesPerKind)
                    break;
                intake.Sketches.Add(new IntakeVisual { Path = IntakeAssets.CopyIn(Catalog.Root, intake.Id, file) });
            }
            PersistIntake();
        }
        catch (Exception ex)
        {
            _native.Warn("無法加入圖片", FirstLine(ex.Message));
        }
    }

    public void PasteIntakeVisual()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        intake.MergeAttachments();
        if (intake.Sketches.Count >= IntakeAssets.MaxFilesPerKind)
        {
            _native.Warn("附件已滿", $"同一進件最多 {IntakeAssets.MaxFilesPerKind} 張。");
            return;
        }
        var dest = Path.Combine(Path.GetTempPath(), $"intake-clip-{Guid.NewGuid():N}.png");
        var err = CursorLauncher.SaveClipboardImageWindows(dest);
        if (err is not null)
        {
            _native.Warn("剪貼簿", err);
            return;
        }
        try
        {
            intake.Sketches.Add(new IntakeVisual { Path = IntakeAssets.CopyIn(Catalog.Root, intake.Id, dest) });
            PersistIntake();
        }
        catch (Exception ex)
        {
            _native.Warn("無法加入圖片", FirstLine(ex.Message));
        }
        finally
        {
            try { File.Delete(dest); } catch (IOException) { }
        }
    }

    public void RemoveIntakeVisual(string path)
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null || string.IsNullOrWhiteSpace(path))
            return;
        intake.Sketches.RemoveAll(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        intake.Crops.RemoveAll(v => string.Equals(v.Path, path, StringComparison.OrdinalIgnoreCase));
        IntakeAssets.DeleteFile(Catalog.Root, path);
        PersistIntake();
    }

    public async Task PauseSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        if (!IntakeGates.CanPause(intake))
        {
            _native.Warn("不能暫停", intake.IsPaused ? "已經暫停。" : "尚未發出，或已驗收／收回。");
            return;
        }
        if (!_native.Confirm(
            "暫停執行",
            $"確定暫停「{intake.Title}」？會在相關 Issue／未合併 PR 留言通知承辦人先停下。Issue 保持開啟，可隨時恢復。"))
            return;
        await ApplyHoldAsync(intake, IntakeHolds.Paused, "pause").ConfigureAwait(false);
    }

    public async Task ResumeSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        if (!IntakeGates.CanResume(intake))
        {
            _native.Warn("不能恢復", "只有已暫停的進件可以恢復。");
            return;
        }
        if (!_native.Confirm("恢復執行", $"確定恢復「{intake.Title}」？會留言通知承辦人繼續。"))
            return;
        await ApplyHoldAsync(intake, "", "resume").ConfigureAwait(false);
    }

    public async Task RecallSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        if (!IntakeGates.CanRecall(intake))
        {
            _native.Warn("不能收回", "尚未發出，或已驗收／已收回。");
            return;
        }
        var merged = IntakeGates.HasMergedWork(intake)
            ? "\n\n已有程式入主線：收回只會通知並關閉 Issue，不會還原程式。"
            : "";
        if (!_native.Confirm(
            "通知收回",
            $"確定收回「{intake.Title}」？會在相關 Issue／未合併 PR 留言，並把 Issue 標成不計畫進行。本進件會留在清單供稽核，不當刪除。{merged}"))
            return;
        await ApplyHoldAsync(intake, IntakeHolds.Recalled, "recall").ConfigureAwait(false);
    }

    async Task ApplyHoldAsync(IntakeRecord intake, string hold, string action)
    {
        if (!GitIssuesReady)
        {
            _native.Warn("認證種類尚未接任務", GitHost.KindLabel(GitKind) + " 還不能留言或關閉 Issue。");
            return;
        }
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        IntakeBusy = true;
        Notify();
        try
        {
            var body = IntakeLifecycle.HoldComment(intake, action, GithubAccount.Login, HoldNoteDraft);
            await CommentIssuedAsync(intake, cfg, body).ConfigureAwait(false);
            if (action == "recall")
            {
                foreach (var item in intake.Items.Where(i => i.HasIssue))
                    await GitHubIssues.CloseAsync(Catalog!.Root, cfg, item.IssueNumber!.Value, _cts.Token, "not planned").ConfigureAwait(false);
            }
            intake.Hold = hold;
            intake.HoldNote = HoldNoteDraft.Trim();
            intake.HoldAt = string.IsNullOrEmpty(hold) ? "" : DateTimeOffset.Now.ToString("o");
            intake.HoldBy = string.IsNullOrEmpty(hold) ? "" : GithubAccount.Login;
            if (string.IsNullOrEmpty(hold))
                intake.HoldNote = "";
            PersistIntake();
            JobText = action switch
            {
                "pause" => "已通知暫停執行",
                "resume" => "已通知恢復執行",
                _ => "已通知收回",
            };
            await RefreshIntakeRemoteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _native.Error(action == "recall" ? "收回失敗" : "通知失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    async Task CommentIssuedAsync(IntakeRecord intake, GithubConfig cfg, string body)
    {
        foreach (var item in intake.Items.Where(i => i.HasIssue))
        {
            var text = body;
            if (!string.IsNullOrWhiteSpace(item.Assignee))
                text = "@" + item.Assignee.Trim().TrimStart('@') + "\n\n" + body;
            await GitHubIssues.CommentAsync(Catalog!.Root, cfg, item.IssueNumber!.Value, text, _cts.Token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(item.PrUrl) || string.Equals(item.PrState, "MERGED", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!int.TryParse(item.PrNumber, out var pr) || pr <= 0)
                pr = GitHubLifecycle.FindIssueNumbers(item.PrUrl).LastOrDefault();
            if (pr <= 0)
                continue;
            try
            {
                await GitHubLifecycle.CommentPrAsync(Catalog.Root, cfg, pr, text, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Issue 留言已成功；PR 留言失敗不回滾
            }
        }
    }

    public async Task PublishSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        if (IntakeGates.BlockWhileHeld(intake) is { } held)
        {
            _native.Warn("還不能發出", held);
            return;
        }
        FillIntakeSlug(intake);
        intake.EnsurePrimaryItem();
        var block = IntakeGates.BlockPublish(intake);
        if (block is not null)
        {
            _native.Warn("還不能發出", block);
            return;
        }
        if (intake.IssuedCount > 0)
        {
            _native.Warn("已經發出", "這筆進件已有 GitHub Issue。");
            return;
        }
        if (!GitIssuesReady)
        {
            _native.Warn("認證種類尚未接任務", GitHost.KindLabel(GitKind) + " 還不能開 Issue。請改用 GitHub 或 GitHub Enterprise。");
            return;
        }
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        if (string.IsNullOrEmpty(cfg.Slug()))
        {
            _native.Warn("沒有目標倉", "目前專案沒有 owner/repo，請先登入遠端 Git。");
            return;
        }
        var preview = IntakeLifecycle.PublishPreview(intake);
        var assignHint = string.IsNullOrWhiteSpace(intake.Assignee)
            ? ""
            : $"\n指派給 {intake.Assignee.Trim().TrimStart('@')}。";
        if (!_native.Confirm(
            "發出 Issue",
            $"要把「{intake.Title}」發到 {cfg.Slug()} 嗎？{assignHint}\n會先把進件表與附件提交並推到遠端，Issue 會附上連結。\n\n預覽：\n{preview}"))
            return;
        IntakeBusy = true;
        Notify();
        try
        {
            PersistIntake();
            var links = await PublishIntakeFilesAsync(intake, cfg).ConfigureAwait(false);
            var item = intake.EnsurePrimaryItem();
            var assignee = string.IsNullOrWhiteSpace(item.Assignee) ? null : item.Assignee;
            var (number, url) = await GitHubIssues.CreateAsync(
                Catalog.Root,
                cfg,
                intake.Title,
                IntakeLifecycle.IssueBody(intake, item, links),
                null,
                assignee,
                _cts.Token).ConfigureAwait(false);
            item.IssueNumber = number;
            item.IssueUrl = url;
            if (!string.IsNullOrWhiteSpace(assignee))
            {
                try
                {
                    await GitHubIssues.AssignAsync(Catalog.Root, cfg, number, assignee, _cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    // 建立已成功；指派失敗不回滾
                }
            }
            PersistIntake();
            JobText = "已發出 Issue";
            await RefreshIntakeRemoteAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            JobText = "已取消發出";
        }
        catch (Exception ex)
        {
            _native.Error("發出 Issue 失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    public void StampReleaseOnIntake(string tag)
    {
        var intake = SelectedIntake;
        if (intake is null || string.IsNullOrWhiteSpace(tag))
            return;
        intake.ReleaseTag = tag.Trim();
        PersistIntake();
    }

    public async Task AcceptSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        if (IntakeGates.BlockWhileHeld(intake) is { } held)
        {
            _native.Warn("還不能驗收", held);
            return;
        }
        var block = IntakeGates.BlockAccept(intake);
        if (block is not null)
        {
            _native.Warn("還不能驗收", block);
            return;
        }
        if (!_native.Confirm("驗收結案", $"確定關閉「{intake.Title}」的 Issue？"))
            return;
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        IntakeBusy = true;
        Notify();
        try
        {
            foreach (var item in intake.Items.Where(i => i.HasIssue))
                await GitHubIssues.CloseAsync(Catalog.Root, cfg, item.IssueNumber!.Value, _cts.Token).ConfigureAwait(false);
            intake.AcceptedAt = DateTimeOffset.Now.ToString("o");
            PersistIntake();
            JobText = "已驗收";
        }
        catch (Exception ex)
        {
            _native.Error("結案失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    public async Task LoadCollaboratorsAsync()
    {
        if (Catalog is null)
            return;
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            Collaborators = await GitHubIssues.ListCollaboratorsAsync(Catalog.Root, cfg, _cts.Token).ConfigureAwait(false);
        }
        catch
        {
            Collaborators = [];
        }
        Notify();
    }

    async Task RefreshIntakeRemoteAsync()
    {
        if (Catalog is null || IntakeDoc.Intakes.Count == 0)
            return;
        IntakeBusy = true;
        Notify();
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            var numbers = IntakeDoc.Intakes.SelectMany(i => i.Items)
                .Where(i => i.HasIssue)
                .Select(i => i.IssueNumber!.Value)
                .Distinct()
                .ToList();
            if (numbers.Count == 0)
            {
                DeriveLocalStages();
                return;
            }
            if (GitIssuesReady && GithubLoggedIn)
            {
                try
                {
                    GovernanceIssues = await GitHubIssues.ListAsync(Catalog.Root, cfg, "open", 80, _cts.Token).ConfigureAwait(false);
                }
                catch
                {
                    GovernanceIssues = [];
                }
            }
            var traces = await GitHubLifecycle.LoadTracesAsync(Catalog.Root, cfg, numbers, _cts.Token).ConfigureAwait(false);
            var map = traces.ToDictionary(t => t.Number);
            foreach (var intake in IntakeDoc.Intakes)
            {
                foreach (var item in intake.Items)
                {
                    if (item.IssueNumber is int n && map.TryGetValue(n, out var tr) && !string.IsNullOrEmpty(tr.PrUrl))
                    {
                        item.PrUrl = tr.PrUrl;
                        item.PrState = tr.PrState;
                        item.PrNumber = GitHubLifecycle.FindIssueNumbers(tr.PrUrl).LastOrDefault().ToString();
                        if (item.PrNumber == "0")
                            item.PrNumber = "";
                    }
                }
                IntakeLifecycle.ApplyTraces(intake, map);
            }
            IntakeStore.Save(Catalog.Root, IntakeDoc);
            if (Collaborators.Count == 0)
                await LoadCollaboratorsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IntakeHint = FirstLine(ex.Message);
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    void DeriveLocalStages()
    {
        foreach (var intake in IntakeDoc.Intakes)
        {
            var (stage, block) = IntakeLifecycle.Derive(intake);
            intake.Stage = stage;
            intake.BlockReason = block;
        }
    }

    void FillIntakeSlug(IntakeRecord intake)
    {
        if (!string.IsNullOrWhiteSpace(intake.GithubSlug) && intake.GithubSlug.Contains('/', StringComparison.Ordinal))
            return;
        var cfg = GithubDraft;
        if (string.IsNullOrEmpty(cfg.Slug()))
            return;
        intake.GithubSlug = cfg.Slug();
        intake.Host = cfg.ResolvedHost();
    }

    async Task<IntakeIssueLinks?> PublishIntakeFilesAsync(IntakeRecord intake, GithubConfig cfg)
    {
        var paths = IntakeLifecycle.PublishRelPaths(intake);
        try
        {
            await GitHubService.CommitPathsIfDirtyAsync(
                Catalog!.Root,
                $"intake: {intake.Id} {intake.Title}".Trim(),
                paths).ConfigureAwait(false);
            await GitHubService.PublishBranchAsync(Catalog, cfg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_native.Confirm(
                "文件尚未上遠端",
                FirstLine(ex.Message) + "\n\n仍要發出 Issue？其他人暫時可能打不開附件連結。"))
                throw new OperationCanceledException();
        }
        var (code, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], Catalog!.Root).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(branch) || branch == "HEAD")
            branch = string.IsNullOrWhiteSpace(cfg.DefaultBranch) ? "main" : cfg.DefaultBranch;
        var web = cfg.WebUrl();
        return string.IsNullOrEmpty(web) ? null : new IntakeIssueLinks(web, branch);
    }

    async Task<GithubConfig> ResolveIntakeConfigAsync(IntakeRecord intake)
    {
        var cfg = Catalog is null ? new GithubConfig() : await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(intake.GithubSlug) && intake.GithubSlug.Contains('/', StringComparison.Ordinal))
        {
            var parts = intake.GithubSlug.Split('/', 2);
            cfg.Owner = parts[0];
            cfg.Repo = parts[1];
        }
        if (!string.IsNullOrWhiteSpace(intake.Host))
            cfg.Host = GitHost.Normalize(intake.Host);
        else
            cfg.Host = ActiveGitHost;
        return cfg;
    }
}
