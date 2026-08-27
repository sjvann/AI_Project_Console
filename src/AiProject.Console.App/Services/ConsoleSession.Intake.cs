using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Intake;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;
using AiProject.Console.Core.WorkHours;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession
{
    public IntakeDocument IntakeDoc { get; private set; } = new();
    public string? SelectedIntakeId { get; private set; }
    public string IntakeFilter { get; set; } = "all";
    public string IntakeHint { get; private set; } = "";
    public bool IntakeBusy { get; private set; }
    public IReadOnlyList<string> Collaborators { get; private set; } = [];
    public IReadOnlyList<GithubIssue> GovernanceIssues { get; private set; } = [];
    public IReadOnlyList<HoursInboxMatch> HoursInbox { get; private set; } = [];
    public string HoursInboxHint { get; private set; } = "";
    public string ReviewerDraft { get; set; } = "";
    public IReadOnlyList<string> DesignDocChoices { get; private set; } = [];
    public IReadOnlyList<string> ProductLineChoices =>
        Catalog is null
            ? []
            : ServiceCatalogBuilder.ReadProductLines(Catalog.Manifest, Catalog.Services).Select(l => l.Label).ToList();

    public IntakeRecord? SelectedIntake =>
        IntakeStore.Find(IntakeDoc, SelectedIntakeId);

    public IReadOnlyList<IntakeRecord> VisibleIntakes
    {
        get
        {
            IEnumerable<IntakeRecord> q = IntakeDoc.Intakes;
            q = IntakeFilter switch
            {
                "requirement" => q.Where(i => i.Kind == IntakeKinds.Requirement),
                "design-change" => q.Where(i => i.Kind == IntakeKinds.DesignChange),
                _ => q,
            };
            return q.ToList();
        }
    }

    public async Task RefreshIntakeAsync()
    {
        if (Catalog is null)
        {
            IntakeDoc = new();
            SelectedIntakeId = null;
            IntakeHint = "請先選擇專案或薄工作區。需求表寫在 docs/product/intake.json。";
            Notify();
            return;
        }
        IntakeDoc = IntakeStore.Load(Catalog.Root);
        if (SelectedIntake is null)
            SelectedIntakeId = IntakeDoc.Intakes.FirstOrDefault()?.Id;
        RefreshDesignDocChoices();
        DeriveLocalStages();
        Notify();
        await RefreshIntakeRemoteAsync().ConfigureAwait(false);
    }

    public void SelectIntake(string? id)
    {
        SelectedIntakeId = id;
        Notify();
    }

    public void SetIntakeFilter(string filter)
    {
        IntakeFilter = filter;
        Notify();
    }

    public void AddIntake(string kind)
    {
        if (Catalog is null)
        {
            _native.Warn("需求工作台", "請先選擇專案目錄。");
            return;
        }
        var record = IntakeStore.NewRecord(kind, GithubAccount.Login);
        var cfg = GithubDraft;
        if (!string.IsNullOrEmpty(cfg.Slug()))
        {
            record.GithubSlug = cfg.Slug();
            record.Host = cfg.ResolvedHost();
        }
        IntakeDoc.Intakes.Insert(0, record);
        SelectedIntakeId = record.Id;
        PersistIntake();
    }

    public void AddWorkItem()
    {
        var intake = SelectedIntake;
        if (intake is null)
            return;
        var item = IntakeStore.NewWorkItem();
        item.GithubSlug = intake.GithubSlug;
        intake.Items.Add(item);
        PersistIntake();
    }

    public void RemoveWorkItem(string id)
    {
        var intake = SelectedIntake;
        if (intake is null)
            return;
        intake.Items.RemoveAll(i => i.Id == id);
        PersistIntake();
    }

    public void ToggleDesignDoc(string rel)
    {
        var intake = SelectedIntake;
        if (intake is null || string.IsNullOrWhiteSpace(rel))
            return;
        if (intake.DesignDocs.Any(d => string.Equals(d, rel, StringComparison.OrdinalIgnoreCase)))
            intake.DesignDocs.RemoveAll(d => string.Equals(d, rel, StringComparison.OrdinalIgnoreCase));
        else
            intake.DesignDocs.Add(rel);
        PersistIntake();
    }

    public void ToggleAcceptance(IntakeWorkItem item, int index)
    {
        while (item.AcceptanceDone.Count <= index)
            item.AcceptanceDone.Add(false);
        item.AcceptanceDone[index] = !item.AcceptanceDone[index];
        PersistIntake();
    }

    public void PersistIntake()
    {
        if (Catalog is null)
            return;
        DeriveLocalStages();
        IntakeStore.Save(Catalog.Root, IntakeDoc);
        Notify();
    }

    public async Task PublishSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        var block = IntakeGates.BlockPublish(intake);
        if (block is not null)
        {
            _native.Warn("還不能發出", block);
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
            _native.Warn("沒有目標倉", "請填 owner/repo。");
            return;
        }
        var preview = IntakeLifecycle.PublishPreview(intake);
        if (!_native.Confirm(
            "發出 Issue",
            $"要把「{intake.Title}」的未發出任務發到 {cfg.Slug()} 嗎？\n\n預覽：\n{preview}"))
            return;
        IntakeBusy = true;
        Notify();
        try
        {
            var label = intake.IsDesignChange ? "design-change" : "requirement";
            foreach (var item in intake.Items.Where(i => !i.HasIssue))
            {
                var (number, url) = await GitHubIssues.CreateAsync(
                    Catalog.Root,
                    cfg,
                    item.Title,
                    IntakeLifecycle.IssueBody(intake, item),
                    [label, "task"],
                    string.IsNullOrWhiteSpace(item.Assignee) ? null : item.Assignee,
                    _cts.Token).ConfigureAwait(false);
                item.IssueNumber = number;
                item.IssueUrl = url;
                if (!string.IsNullOrWhiteSpace(item.Assignee))
                {
                    try
                    {
                        await GitHubIssues.AssignAsync(Catalog.Root, cfg, number, item.Assignee, _cts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 建立已成功；指派失敗不回滾
                    }
                }
            }
            PersistIntake();
            JobText = "已發出 Issue";
            await RefreshIntakeRemoteAsync().ConfigureAwait(false);
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

    public async Task RequestReviewSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        var prs = intake.Items.Where(i => !string.IsNullOrEmpty(i.PrUrl)).ToList();
        if (prs.Count == 0)
        {
            _native.Warn("沒有 PR", "這個進件還沒有連結的合併請求。");
            return;
        }
        if (string.IsNullOrWhiteSpace(ReviewerDraft))
        {
            _native.Warn("請指定審查人", "填 GitHub 帳號後再請人審。");
            return;
        }
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        IntakeBusy = true;
        Notify();
        try
        {
            foreach (var item in prs)
            {
                if (!int.TryParse(item.PrNumber, out var n) || n <= 0)
                    n = GitHubLifecycle.FindIssueNumbers(item.PrUrl).LastOrDefault();
                if (n <= 0)
                    continue;
                await GitHubLifecycle.RequestReviewAsync(Catalog.Root, cfg, n, ReviewerDraft, _cts.Token).ConfigureAwait(false);
            }
            JobText = "已請人審查";
        }
        catch (Exception ex)
        {
            _native.Error("請人審失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    public async Task MergeSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        var mergeBlock = IntakeGates.BlockMerge(intake);
        if (mergeBlock is not null)
        {
            _native.Warn("還不能合併", mergeBlock);
            return;
        }
        if (!_native.Confirm("合併 PR", "確定把這個進件相關、檢查已過的 PR 合併進主線？"))
            return;
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        IntakeBusy = true;
        Notify();
        try
        {
            foreach (var item in intake.Items)
            {
                if (!int.TryParse(item.PrNumber, out var n) || n <= 0)
                    n = GitHubLifecycle.FindIssueNumbers(item.PrUrl).LastOrDefault();
                if (n <= 0 || item.CiTone == "warn")
                    continue;
                await GitHubLifecycle.MergeAsync(Catalog.Root, cfg, n, allowWhenRed: false, _cts.Token).ConfigureAwait(false);
            }
            JobText = "已合併";
            await RefreshIntakeRemoteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _native.Error("合併失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    public async Task ReleaseSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        ReleaseNotes = IntakeLifecycle.ReleaseNotes(intake);
        ReleaseTitle = intake.Id + " " + intake.Title;
        Dialog = "release";
        Notify();
        await Task.CompletedTask;
    }

    public void StampReleaseOnIntake(string tag)
    {
        var intake = SelectedIntake;
        if (intake is null || string.IsNullOrWhiteSpace(tag))
            return;
        intake.ReleaseTag = tag.Trim();
        PersistIntake();
    }

    public async Task DeploySelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        var deploy = DeployConfigResolver.Resolve(Catalog);
        var workflow = deploy.Target switch
        {
            DeployTargets.Gcp => deploy.Gcp.Workflow,
            DeployTargets.Azure => deploy.Azure.Workflow,
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(workflow))
        {
            if (_native.Confirm("沒有部署 workflow", "這個進件要標成「不部署」並繼續嗎？"))
            {
                intake.SkipDeploy = true;
                PersistIntake();
            }
            return;
        }
        var cfg = await ResolveIntakeConfigAsync(intake).ConfigureAwait(false);
        IntakeBusy = true;
        Notify();
        try
        {
            var msg = await GitHubLifecycle.DispatchWorkflowAsync(Catalog.Root, cfg, workflow, _cts.Token).ConfigureAwait(false);
            var run = await GitHubLifecycle.LatestRunAsync(Catalog.Root, cfg, workflow, _cts.Token).ConfigureAwait(false);
            intake.DeployRunId = run is { DatabaseId: > 0 } ? run.DatabaseId.ToString() : DateTimeOffset.Now.ToString("o");
            intake.DeployHint = run is null
                ? msg
                : $"{run.DisplayTitle} · {run.Status} {run.Conclusion}".Trim();
            PersistIntake();
            JobText = string.IsNullOrEmpty(run?.Url) ? "已觸發部署" : "已觸發部署 " + run.Url;
        }
        catch (Exception ex)
        {
            _native.Error("部署失敗", FirstLine(ex.Message));
        }
        finally
        {
            IntakeBusy = false;
            Notify();
        }
    }

    public async Task AcceptSelectedAsync()
    {
        var intake = SelectedIntake;
        if (Catalog is null || intake is null)
            return;
        var block = IntakeGates.BlockAccept(intake);
        if (block is not null)
        {
            _native.Warn("還不能驗收", block);
            return;
        }
        if (!_native.Confirm("驗收結案", $"確定依驗收條件關閉「{intake.Title}」的 Issue？"))
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

    public async Task ImportHoursInboxAsync()
    {
        var files = await _native.PickFilesAsync("選擇工時 CSV", ("CSV", [".csv"])).ConfigureAwait(false);
        if (files is null || files.Length == 0)
            return;
        var items = new List<HoursInboxRow>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            if (text.Contains("kind,number,title", StringComparison.OrdinalIgnoreCase))
                items.AddRange(WorkHoursInbox.ParseItemsCsv(text));
            else
                items.AddRange(WorkHoursInbox.ParseHoursCsv(text));
        }
        HoursInbox = WorkHoursInbox.Match(items.Where(i => i.Number > 0), IntakeDoc.Intakes);
        var matched = HoursInbox.Count(m => !string.IsNullOrEmpty(m.IntakeId));
        HoursInboxHint = $"讀入 {HoursInbox.Count} 筆 Issue／PR，對上 {matched} 筆進件。";
        foreach (var group in HoursInbox.Where(m => m.IntakeId is not null).GroupBy(m => m.IntakeId))
        {
            var intake = IntakeStore.Find(IntakeDoc, group.Key);
            if (intake is not null)
                intake.Billed = true;
        }
        PersistIntake();
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

    void RefreshDesignDocChoices()
    {
        if (Catalog is null)
        {
            DesignDocChoices = [];
            return;
        }
        try
        {
            var docs = DocsService.Scan(Catalog.Root).Files
                .Select(f => f.RelPath)
                .Where(p => p.StartsWith("product/", StringComparison.OrdinalIgnoreCase)
                    || p.StartsWith("engineering/", StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToList();
            DesignDocChoices = docs;
        }
        catch
        {
            DesignDocChoices = [];
        }
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
