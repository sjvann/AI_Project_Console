using System.Text;
using AiProject.Console.Core;
using AiProject.Console.Core.Actions;
using AiProject.Console.Core.Build;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Update;
using AiProject.Console.Core.Util;
using Microsoft.JSInterop;
using Photino.NET;

namespace AiProject.Console.App.Services;

public sealed class ConsoleSession : IDisposable
{
    private readonly NativeUi _native;
    private readonly CancellationTokenSource _cts = new();
    private long _logOffset;

    public ConsoleSession(NativeUi native)
    {
        _native = native;
        OpenWithCursor = ConsoleSettingsStore.GetOpenWithCursor();
        RestoreLastProject = ConsoleSettingsStore.GetRestoreLastProject();
        _ = PollLoopAsync();
        _ = CheckUpdateOnStartAsync();
        _ = RestoreLastProjectOnStartAsync();
    }

    public IJSRuntime? Js { get; set; }

    public event Action? Changed;

    public ProjectCatalog? Catalog { get; private set; }
    public ProjectRuntime? Runtime { get; private set; }
    public IReadOnlyList<string> RecentProjects => ConsoleSettingsStore.RecentProjects();
    public bool OpenWithCursor { get; set; }
    public bool RestoreLastProject { get; set; }
    public string ReadyText { get; private set; } = "就緒 0 / 0";
    public string JobText { get; private set; } = "待命";
    public string WarnText { get; private set; } = "";
    public string GitStatusText { get; private set; } = "";
    public string LogFilter { get; private set; } = "";
    public Dictionary<string, string> StartErrors { get; } = new();
    public bool JobBusy { get; private set; }
    public string LeftTab { get; set; } = "svc";
    public string RightTab { get; set; } = "log";
    public string? SelectedServiceId { get; private set; }
    public Dictionary<string, bool> Health { get; } = new();
    public IReadOnlyList<BuildState> Projects { get; private set; } = [];
    public bool StaleOnly { get; set; }
    public bool FollowLog { get; set; } = true;
    public string LogTitle { get; private set; } = "Log · （未選服務）";
    public string LogText { get; private set; } = "";
    public string VisibleLogText => TextFilter.Apply(LogText, LogFilter);
    public string BuildText { get; private set; } = "";
    public bool CompileHelpEnabled { get; private set; }
    public BuildFailure? LastBuildFailure { get; private set; }

    public string? Dialog { get; private set; }
    public string AlertTitle { get; private set; } = "";
    public string AlertBody { get; private set; } = "";
    public GithubConfig GithubDraft { get; private set; } = new();
    public string GithubSaveTarget { get; set; } = "local";
    public bool GithubApplyRemote { get; set; } = true;
    public DeployConfig DeployDraft { get; private set; } = new();
    public string DeploySaveTarget { get; set; } = "local";
    public string UatTitle { get; set; } = "";
    public string UatDescription { get; set; } = "";
    public List<string> UatImages { get; } = [];
    public string ConfirmTitle { get; private set; } = "";
    public string ConfirmBody { get; private set; } = "";
    public Func<Task>? ConfirmAction { get; private set; }
    public string AgentPrompt { get; private set; } = "";
    public string AgentIntro { get; private set; } = "";
    public string CloneSpec { get; set; } = "";
    public string CloneParent { get; set; } = "";
    public string CloneFolder { get; set; } = "";
    public string CloneBranch { get; set; } = "";
    public string ReleaseTag { get; set; } = "";
    public string ReleaseTitle { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string ReleaseTarget { get; set; } = "";
    public string ReleaseHint { get; private set; } = "";
    public string ReleaseLatestTag { get; private set; } = "";
    public string ReleaseBasisTag { get; private set; } = "";
    public bool ReleaseDraft { get; set; }
    public bool ReleasePrerelease { get; set; }
    public bool ReleaseGenerateNotes { get; set; } = true;
    public bool ReleaseMakeLatest { get; set; } = true;
    public List<string> ReleaseAssets { get; } = [];
    public string CommitMessage { get; set; } = "";
    public string CommitHint { get; private set; } = "";
    public bool CommitPushAfter { get; set; }
    public IReadOnlyList<GitChange> CommitChanges { get; private set; } = [];
    public string CommitSuggestHint { get; private set; } = "";
    public GitBriefStatus? GitBrief { get; private set; }
    public bool HasUncommitted => GitBrief is { DirtyCount: > 0 };
    public AvailableUpdate? UpdateAvailable { get; private set; }

    public IReadOnlyList<ConsoleAction> BuildActions => ActionCatalog.Load("build");
    public IReadOnlyList<ConsoleAction> GithubActions => ActionCatalog.Load("github");
    public IReadOnlyList<ConsoleAction> DeployActions => ActionCatalog.Load("deploy");

    public IEnumerable<IGrouping<string, ServiceEntry>> ServiceGroups =>
        Catalog?.Services.GroupBy(s => string.IsNullOrEmpty(s.Group) ? "其他" : s.Group)
        ?? Enumerable.Empty<IGrouping<string, ServiceEntry>>();

    public IEnumerable<BuildState> VisibleProjects =>
        StaleOnly ? Projects.Where(p => p.Status is "stale" or "unbuilt") : Projects;

    public IEnumerable<IGrouping<string, BuildState>> ProjectGroups =>
        VisibleProjects.GroupBy(p => string.IsNullOrEmpty(p.System) ? "其他" : p.System);

    public int ReadyCount => Catalog is null ? 0 : Catalog.Services.Count(s => Health.GetValueOrDefault(s.Id));
    public int ServiceCount => Catalog?.Services.Count ?? 0;
    public int StaleProjectCount => Projects.Count(p => p.Status is "stale" or "unbuilt");

    public void SetOpenWithCursor(bool value)
    {
        OpenWithCursor = value;
        ConsoleSettingsStore.SetOpenWithCursor(value);
        Notify();
    }

    public void SetRestoreLastProject(bool value)
    {
        RestoreLastProject = value;
        ConsoleSettingsStore.SetRestoreLastProject(value);
        Notify();
    }

    public void SetLogFilter(string value)
    {
        LogFilter = value ?? "";
        Notify();
    }

    public async Task PickProjectAsync()
    {
        var path = await _native.PickFolderAsync().ConfigureAwait(false);
        if (!string.IsNullOrEmpty(path))
            await LoadProjectAsync(path, OpenWithCursor).ConfigureAwait(false);
    }

    public Task LoadRecentAsync(string path) => LoadProjectAsync(path, OpenWithCursor);

    public bool HasProject => Catalog is not null;

    public Task CloseProjectAsync()
    {
        if (Catalog is null)
        {
            _native.Info("尚未選擇專案", "目前沒有開啟的專案。");
            return Task.CompletedTask;
        }
        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return Task.CompletedTask;
        }

        var name = Catalog.Name;
        var running = Catalog.Services.Where(s => Health.GetValueOrDefault(s.Id)).Select(s => s.Label).ToList();
        var body = running.Count > 0
            ? $"確定關閉「{name}」？\n目前有 {running.Count} 個服務在執行，關閉時會一併停止，並回到尚未選擇專案的狀態。"
            : $"確定關閉「{name}」？\n會回到尚未選擇專案的狀態。";
        if (!_native.Confirm("關閉專案", body))
            return Task.CompletedTask;

        var closeIde = _native.Confirm("關閉 Cursor", "要一併關閉 Cursor 嗎？");
        if (Catalog is not null && Runtime is not null)
        {
            try
            {
                ProcessSupervisor.StopAll(Catalog, Runtime);
            }
            catch (Exception ex)
            {
                if (!_native.Confirm("停止服務失敗", $"停止服務時發生問題：\n{ex.Message}\n\n仍要關閉專案嗎？"))
                    return Task.CompletedTask;
            }
        }

        if (closeIde)
        {
            var err = CursorLauncher.CloseCursor();
            if (err is not null)
                _native.Warn("關閉 Cursor", err);
        }

        ResetToStartup();
        try
        {
            ConsoleSettingsStore.ClearLastProject();
        }
        catch
        {
            // 歷史仍保留，還原標記失敗不阻擋關閉
        }
        JobText = closeIde ? "已關閉專案，並關閉 Cursor" : "已關閉專案";
        Notify();
        return Task.CompletedTask;
    }

    public Task LoadProjectAsync(string root, bool openCursor = false)
    {
        try
        {
            root = Path.GetFullPath(root);
            if (!TryStopCurrentProjectForSwitch(root))
                return Task.CompletedTask;

            var catalog = ServiceCatalogBuilder.Build(root);
            Catalog = catalog;
            Runtime = new ProjectRuntime(catalog.Root);
            Runtime.Ensure();
            string? rememberErr = null;
            try
            {
                ConsoleSettingsStore.RememberProject(catalog.Root);
            }
            catch (Exception ex)
            {
                rememberErr = ex.Message;
            }
            SelectedServiceId = catalog.Services.FirstOrDefault()?.Id;
            LastBuildFailure = null;
            CompileHelpEnabled = false;
            Health.Clear();
            StartErrors.Clear();
            LogFilter = "";
            GitStatusText = "";
            ReloadLog();
            JobText = rememberErr is null ? "已載入專案" : $"已載入專案（歷史未寫入：{rememberErr}）";
            Notify();
            _ = RefreshBuildStatesAsync();
            _ = RefreshGitStatusAsync();
            if (openCursor)
            {
                var err = CursorLauncher.OpenInCursor(catalog.Root);
                JobText = err is null
                    ? (rememberErr is null ? "已載入專案，並在 Cursor 開啟" : JobText)
                    : $"已載入專案（Cursor 未開啟：{err}）";
                Notify();
            }
        }
        catch (Exception ex)
        {
            _native.Error("開啟專案失敗", ex.Message);
        }
        return Task.CompletedTask;
    }

    private bool TryStopCurrentProjectForSwitch(string nextRoot)
    {
        if (Catalog is null || Runtime is null)
            return true;
        if (string.Equals(Catalog.Root, nextRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var running = Catalog.Services.Where(s => Health.GetValueOrDefault(s.Id)).Select(s => s.Label).ToList();
        if (running.Count > 0)
        {
            if (!_native.Confirm(
                "切換專案",
                $"將關閉「{Catalog.Name}」並開啟新專案。\n目前有 {running.Count} 個服務在執行，會先停止。\n\n確定切換？"))
                return false;
        }

        try
        {
            ProcessSupervisor.StopAll(Catalog, Runtime);
        }
        catch (Exception ex)
        {
            if (!_native.Confirm("停止服務失敗", $"停止舊專案服務時發生問題：\n{ex.Message}\n\n仍要開啟新專案嗎？"))
                return false;
        }
        return true;
    }

    public void SelectService(string id)
    {
        SelectedServiceId = id;
        ReloadLog();
        Notify();
    }

    public void ReloadLog()
    {
        _logOffset = 0;
        LogText = "";
        var svc = SelectedService();
        LogTitle = svc is null ? "Log · （未選服務）" : $"Log · {svc.Label}";
        AppendLogTail(full: true);
        Notify();
    }

    public void ClearLogView()
    {
        LogText = "";
        Notify();
    }

    public void OpenLogDir()
    {
        if (Runtime is null)
            return;
        Runtime.Ensure();
        CliUtil.OpenPath(Runtime.Logs);
    }

    public async Task StartAllAsync()
    {
        if (!RequireCatalog())
            return;
        if (!CliUtil.CommandExists("dotnet"))
        {
            _native.Error("缺少工具", "找不到 dotnet。");
            return;
        }
        var catalog = Catalog!;
        var runtime = Runtime!;
        var health = new Dictionary<string, bool>(Health);
        IReadOnlyList<(string Id, string Label, string? Error)> results = [];
        await RunJobAsync("啟動中…", () =>
        {
            results = ProcessSupervisor.StartOffline(catalog, runtime, health);
            return Task.FromResult<string?>(null);
        }).ConfigureAwait(false);
        ApplyStartResults(results);
        if (results.Count == 0 && !JobBusy)
            JobText = "所有服務已在線";
        Notify();
    }

    public Task StartOneAsync(ServiceEntry svc)
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        if (!string.IsNullOrEmpty(svc.HostedBy))
        {
            _native.Info("隨宿主啟動", $"「{svc.Label}」隨 {svc.HostedBy} 一併提供，請啟動宿主服務。");
            return Task.CompletedTask;
        }
        if (Health.GetValueOrDefault(svc.Id))
        {
            _native.Info("已在線", $"「{svc.Label}」已在執行。");
            return Task.CompletedTask;
        }
        if (!CliUtil.CommandExists("dotnet"))
        {
            _native.Error("缺少工具", "找不到 dotnet。");
            return Task.CompletedTask;
        }
        var catalog = Catalog!;
        var runtime = Runtime!;
        return RunJobAsync($"啟動 {svc.Label}…", () =>
        {
            var err = ProcessSupervisor.TryStartService(catalog, runtime, svc);
            ApplyStartResults([(svc.Id, svc.Label, err)]);
            return Task.FromResult(err);
        });
    }

    public Task StopAllAsync()
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var runtime = Runtime!;
        return RunJobAsync("停止中…", () =>
        {
            ProcessSupervisor.StopAll(catalog, runtime);
            return Task.FromResult<string?>(null);
        });
    }

    public Task StopOneAsync(ServiceEntry svc)
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var runtime = Runtime!;
        return RunJobAsync($"停止 {svc.Label}…", () =>
        {
            ProcessSupervisor.StopService(catalog, runtime, svc);
            return Task.FromResult<string?>(null);
        });
    }

    public Task RestartOneAsync(ServiceEntry svc)
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var runtime = Runtime!;
        return RunJobAsync($"重啟 {svc.Label}…", () =>
        {
            ProcessSupervisor.RestartService(catalog, runtime, svc);
            return Task.FromResult<string?>(null);
        });
    }

    public void OpenUrls(bool frontendsOnly)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        List<ServiceEntry> targets;
        if (frontendsOnly)
        {
            var svc = string.IsNullOrEmpty(catalog.Frontend) ? null : ServiceCatalogBuilder.ById(catalog, catalog.Frontend);
            targets = svc is not null && !string.IsNullOrEmpty(svc.OpenUrl)
                ? [svc]
                : catalog.Services.Where(s => !string.IsNullOrEmpty(s.OpenUrl)).Take(1).ToList();
        }
        else
            targets = catalog.Services.Where(s => !string.IsNullOrEmpty(s.OpenUrl)).ToList();
        var opened = 0;
        foreach (var svc in targets)
        {
            if (!string.IsNullOrEmpty(svc.OpenUrl))
            {
                CliUtil.OpenUrl(svc.OpenUrl);
                opened++;
            }
        }
        if (opened == 0)
            _native.Info("無 URL", "沒有可開啟的 openUrl。");
    }

    public void OpenServiceUrl(ServiceEntry svc)
    {
        if (!string.IsNullOrEmpty(svc.OpenUrl))
            CliUtil.OpenUrl(svc.OpenUrl);
    }

    public void Doctor() => _native.Info("環境體檢", ProcessSupervisor.DoctorReport(Catalog));

    public Task CheckUpdateAsync() =>
        RunJobAsync("檢查更新…", async () =>
        {
            var update = await SelfUpdate.CheckLatestAsync(_cts.Token).ConfigureAwait(false);
            ConsoleSettingsStore.MarkUpdateChecked();
            if (update is null)
            {
                UpdateAvailable = null;
                return $"目前已是最新版本 v{AppInfo.Version}。";
            }
            UpdateAvailable = update;
            Notify();
            return $"發現新版本 {update.Tag}（目前 v{AppInfo.Version}）。可按「立即更新」下載並安裝。";
        });

    public void DismissUpdate()
    {
        if (UpdateAvailable is not null)
            ConsoleSettingsStore.SetSkippedUpdateTag(UpdateAvailable.Tag);
        UpdateAvailable = null;
        Notify();
    }

    public void OpenUpdatePage()
    {
        SelfUpdate.OpenReleases(UpdateAvailable?.HtmlUrl);
    }

    public async Task ApplyUpdateAsync()
    {
        var update = UpdateAvailable;
        if (update is null)
        {
            await CheckUpdateAsync().ConfigureAwait(false);
            update = UpdateAvailable;
            if (update is null)
                return;
        }

        var kind = SelfUpdate.DetectInstallKind();
        var mode = SelfUpdate.ResolveApplyMode(update, kind);
        if (mode is UpdateApplyMode.None or UpdateApplyMode.OpenReleases)
        {
            SelfUpdate.OpenReleases(update.HtmlUrl);
            _native.Info("無法自動覆蓋", SelfUpdate.DevelopmentHint(update));
            return;
        }

        var asset = mode == UpdateApplyMode.Installer ? update.SetupAsset : update.ZipAsset;
        if (asset is null)
        {
            SelfUpdate.OpenReleases(update.HtmlUrl);
            _native.Info("找不到安裝包", $"Release {update.Tag} 沒有適用於 {SelfUpdate.RuntimeId()} 的安裝檔。請從 Releases 頁手動下載。");
            return;
        }

        if (!_native.Confirm(
            "更新控制台",
            $"將下載並安裝 {update.Tag}（目前 v{AppInfo.Version}）。\n控制台會先關閉以便覆蓋檔案，安裝完成後會自動重開。\n已啟動的專案服務不會自動停止。\n\n確定更新？"))
            return;

        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return;
        }

        JobBusy = true;
        JobText = "下載更新…";
        Notify();
        try
        {
            var progress = new Progress<string>(text =>
            {
                JobText = text;
                Notify();
            });
            var downloaded = await SelfUpdate.DownloadAssetAsync(update, asset, progress, _cts.Token).ConfigureAwait(false);
            JobText = "套用更新…";
            Notify();
            SelfUpdate.LaunchApply(downloaded, mode);
            _native.Close();
        }
        catch (Exception ex)
        {
            JobBusy = false;
            JobText = "錯誤";
            Notify();
            _native.Error("更新失敗", ex.Message);
        }
    }

    private async Task CheckUpdateOnStartAsync()
    {
        try
        {
            await Task.Delay(1500, _cts.Token).ConfigureAwait(false);
            if (!ConsoleSettingsStore.ShouldAutoCheckUpdate(TimeSpan.FromHours(6)))
                return;
            var update = await SelfUpdate.CheckLatestAsync(_cts.Token).ConfigureAwait(false);
            ConsoleSettingsStore.MarkUpdateChecked();
            if (update is null)
                return;
            if (string.Equals(ConsoleSettingsStore.SkippedUpdateTag(), update.Tag, StringComparison.OrdinalIgnoreCase))
                return;
            UpdateAvailable = update;
            Notify();
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch
        {
            // 背景檢查失敗不打擾使用者
        }
    }

    public async Task OnActionAsync(ConsoleAction action)
    {
        var handler = action.Handler;
        if (action.RequiresDeploy && handler is not "deploy_settings" and not "gcp_settings")
        {
            if (!await EnsureDeployAsync().ConfigureAwait(false))
                return;
        }
        if (action.RequiresGithub && handler != "github_settings")
        {
            if (!await EnsureGithubAsync().ConfigureAwait(false))
                return;
        }
        if (!string.IsNullOrEmpty(action.Confirm))
        {
            if (!_native.Confirm("確認", action.Confirm))
                return;
        }

        switch (handler)
        {
            case "deploy_settings":
            case "gcp_settings":
                await EditDeployAsync().ConfigureAwait(false);
                return;
            case "deploy_status":
            case "gcp_status":
                _native.Info("部署狀態", DeployConfigResolver.StatusReport(Catalog));
                return;
            case "deploy_open":
            case "gcp_open":
            {
                var cfg = DeployConfigResolver.Resolve(Catalog);
                if (cfg.NormalizedTarget() == DeployTargets.None)
                {
                    _native.Info("不下發", "目前選擇不下發（僅本機）。若要開啟線上，請先改選發佈目標。");
                    return;
                }
                if (!DeployConfigResolver.OpenHost(cfg))
                    _native.Info("未設定網址", "請在部署設定中填寫對外網址或主機。");
                return;
            }
            case "deploy_ci_hint":
            case "gcp_ci_hint":
                _native.Info("部署說明", DeployConfigResolver.CiHint(Catalog));
                return;
            case "github_clone":
                OpenCloneDialog();
                return;
            case "github_settings":
                await EditGithubAsync().ConfigureAwait(false);
                return;
            case "github_status":
                _native.Info("GitHub 狀態", await GitHubService.StatusReportAsync(Catalog).ConfigureAwait(false));
                return;
            case "github_commit":
                await OpenCommitDialogAsync().ConfigureAwait(false);
                return;
            case "github_open":
                if (Catalog is null || !await GitHubService.OpenOnGithubAsync(Catalog).ConfigureAwait(false))
                    _native.Info("無法開啟", "請先完成 GitHub 設定（owner/repo）。");
                return;
            case "github_sync":
            {
                var dirtyN = await GitHubService.DirtyCountAsync(Catalog!.Root).ConfigureAwait(false);
                if (dirtyN > 0 && !_native.Confirm(
                    "工作區有未提交變更",
                    $"目前有 {dirtyN} 筆未提交變更。pull --rebase 可能失敗。\n\n仍要同步？"))
                    return;
                await RunJobAsync("同步中…", async () => await GitHubService.SyncFromRemoteAsync(Catalog!)).ConfigureAwait(false);
                await RefreshGitStatusAsync().ConfigureAwait(false);
                return;
            }
            case "github_publish":
                await RunJobAsync("發布中…", async () => await GitHubService.PublishBranchAsync(Catalog!)).ConfigureAwait(false);
                return;
            case "github_pr":
                await RunJobAsync("PR…", async () => await GitHubService.CreatePullRequestAsync(Catalog!)).ConfigureAwait(false);
                return;
            case "github_actions":
                await RunJobAsync("Actions…", async () => await GitHubService.WatchActionsAsync(Catalog!)).ConfigureAwait(false);
                return;
            case "github_release":
                await OpenReleaseDialogAsync().ConfigureAwait(false);
                return;
            case "github_releases":
                await RunJobAsync("Release 列表…", async () => await GitHubService.ListReleasesAsync(Catalog!)).ConfigureAwait(false);
                return;
            case "github_open_releases":
                if (Catalog is null || !await GitHubService.OpenReleasesAsync(Catalog).ConfigureAwait(false))
                    _native.Info("無法開啟", "請先完成 GitHub 設定（owner/repo）。");
                return;
            case "console_check_update":
                await CheckUpdateAsync().ConfigureAwait(false);
                return;
            case "build_stale":
            case "build_services":
            case "build_projects":
                await RunBuildActionAsync(handler).ConfigureAwait(false);
                return;
        }
    }

    public Task BuildOneAsync(string relPath)
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var target = Path.Combine(catalog.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
        RightTab = "build";
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Notify();
        return RunJobAsync($"編譯 {Path.GetFileName(relPath)}…", async () =>
        {
            AppendBuild($"=== build {relPath} ===");
            var progress = new Progress<string>(AppendBuild);
            var (code, log) = await BuildRunner.BuildAsync(catalog.Root, target, progress).ConfigureAwait(false);
            AppendBuild($"exit {code}");
            if (code != 0)
                RememberBuildFailure(relPath, code, $"=== build {relPath} ===\n{log}\nexit {code}");
            return (string?)null;
        });
    }

    public void OpenCloneDialog()
    {
        CloneSpec = "";
        CloneFolder = "";
        CloneBranch = "";
        CloneParent = ConsoleSettingsStore.LastCloneParent()
            ?? (RecentProjects.Count > 0 ? Path.GetDirectoryName(RecentProjects[0]) : null)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Dialog = "clone";
        Notify();
    }

    public async Task PickCloneParentAsync()
    {
        var path = await _native.PickFolderAsync("選擇 clone 到哪個資料夾").ConfigureAwait(false);
        if (string.IsNullOrEmpty(path))
            return;
        CloneParent = path;
        Notify();
    }

    public async Task ConfirmCloneAsync()
    {
        var url = GitHubService.NormalizeCloneUrl(CloneSpec);
        if (string.IsNullOrEmpty(url))
        {
            _native.Warn("輸入不完整", "請輸入 GitHub URL 或 owner/repo，例如 acme/app。");
            return;
        }
        if (string.IsNullOrWhiteSpace(CloneParent))
        {
            _native.Warn("輸入不完整", "請選擇本機父目錄。");
            return;
        }
        var folder = string.IsNullOrWhiteSpace(CloneFolder)
            ? GitHubService.SuggestFolderName(CloneSpec)
            : CloneFolder.Trim();
        if (string.IsNullOrEmpty(folder))
        {
            _native.Warn("輸入不完整", "請填寫資料夾名稱。");
            return;
        }
        if (folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _native.Warn("資料夾名稱無效", "請去掉路徑字元，只填資料夾名稱。");
            return;
        }
        var dest = Path.Combine(CloneParent, folder);
        var parent = CloneParent;
        var branch = CloneBranch;
        CloseDialog();
        string? opened = null;
        await RunJobAsync("從 GitHub 開啟…", async () =>
        {
            var result = await GitHubService.CloneRepositoryAsync(url, dest, branch).ConfigureAwait(false);
            opened = result.Path;
            ConsoleSettingsStore.SetLastCloneParent(parent);
            return result.Message;
        }).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(opened) && Directory.Exists(opened))
            await LoadProjectAsync(opened, OpenWithCursor).ConfigureAwait(false);
    }

    public void BumpRelease(string part)
    {
        SemVer source;
        if (ReleaseVersion.TryParse(ReleaseLatestTag, out var published))
            source = published;
        else if (ReleaseVersion.TryParse(ReleaseTag, out var current))
            source = current;
        else
            source = new SemVer(0, 1, 0, "", "v");
        if (string.IsNullOrEmpty(source.Prefix))
            source = source with { Prefix = "v" };
        var next = ReleaseVersion.Bump(source, part).ToTag();
        if (string.IsNullOrWhiteSpace(ReleaseTitle) || ReleaseTitle == ReleaseTag)
            ReleaseTitle = next;
        ReleaseTag = next;
        Notify();
    }

    public async Task AddReleaseAssetAsync()
    {
        var files = await _native.PickFilesAsync(
            "選擇 Release 附件",
            ("安裝包／壓縮檔", [".zip", ".exe", ".msi", ".nupkg", ".tar", ".gz", ".7z"]),
            ("所有檔案", ["*"])).ConfigureAwait(false);
        if (files is null)
            return;
        foreach (var f in files)
        {
            if (!ReleaseAssets.Contains(f))
                ReleaseAssets.Add(f);
        }
        Notify();
    }

    public void RemoveReleaseAsset(string path)
    {
        ReleaseAssets.Remove(path);
        Notify();
    }

    public async Task ConfirmReleaseAsync()
    {
        if (Catalog is null)
            return;
        var tag = ReleaseTag.Trim();
        if (!ReleaseVersion.IsValidTag(tag))
        {
            _native.Warn("版號無效", "請填寫版號／Tag，例如 v1.2.3（不可含空白）。");
            return;
        }
        if (!GitHubService.GhAvailable())
        {
            _native.Error("需要 GitHub CLI", "請安裝 gh：https://cli.github.com/ 並執行 gh auth login。");
            return;
        }
        var title = string.IsNullOrWhiteSpace(ReleaseTitle) ? tag : ReleaseTitle.Trim();
        var kind = ReleaseDraft ? "草稿" : ReleasePrerelease ? "預發行" : "正式發行";
        if (!_native.Confirm("發行 Release", $"將在 GitHub 建立 Release（{kind}）：\n{tag}\n標題：{title}\n\n確定發行？"))
            return;
        var req = new ReleaseRequest(
            Tag: tag,
            Title: title,
            Notes: ReleaseNotes,
            Target: ReleaseTarget,
            Draft: ReleaseDraft,
            Prerelease: ReleasePrerelease,
            GenerateNotes: ReleaseGenerateNotes,
            MakeLatest: ReleaseMakeLatest,
            Assets: [.. ReleaseAssets]);
        CloseDialog();
        await RunJobAsync("發行 Release…", async () => await GitHubService.CreateReleaseAsync(Catalog, req)).ConfigureAwait(false);
    }

    public async Task OpenCommitDialogAsync()
    {
        if (!RequireCatalog())
            return;
        if (!await GitHubService.IsGitRepoAsync(Catalog!.Root).ConfigureAwait(false))
        {
            _native.Info("不是 git 倉庫", "目前專案目錄不是 git 倉庫，無法提交。");
            return;
        }
        IReadOnlyList<GitChange>? changes = null;
        await RunJobAsync("讀取變更…", async () =>
        {
            changes = await GitHubService.ListChangesAsync(Catalog!.Root).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (changes is null)
            return;
        if (changes.Count == 0)
        {
            _native.Info("沒有未提交變更", "工作區是乾淨的，沒有可提交的檔案。");
            return;
        }
        CommitChanges = changes;
        CommitMessage = "";
        CommitPushAfter = false;
        CommitSuggestHint = "";
        CommitHint = $"{changes.Count} 筆未提交變更（將全部加入後提交）";
        Dialog = "commit";
        Notify();
    }

    public async Task SuggestCommitMessageAsync()
    {
        if (!RequireCatalog())
            return;
        if (CommitChanges.Count == 0)
            return;
        if (!string.IsNullOrWhiteSpace(CommitMessage)
            && !_native.Confirm("取代說明", "將用 AI 建議覆蓋目前說明。確定？"))
            return;

        CommitSuggestion? suggestion = null;
        await RunJobAsync("AI 建議說明…", async () =>
        {
            suggestion = await CommitMessageSuggester.SuggestAsync(Catalog!.Root, CommitChanges).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.Message))
        {
            _native.Warn("無法產生建議", "請手動填寫提交說明。");
            return;
        }
        CommitMessage = suggestion.Message;
        CommitSuggestHint = suggestion.Hint;
        Dialog = "commit";
        Notify();
    }

    public async Task ConfirmCommitAsync()
    {
        if (!RequireCatalog())
            return;
        var message = CommitMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            _native.Warn("請填寫說明", "提交說明不可空白。");
            return;
        }
        var n = CommitChanges.Count;
        var push = CommitPushAfter;
        if (!_native.Confirm(
            "提交",
            $"將提交 {n} 筆變更到目前分支。{(push ? "\n提交後會再 push。" : "")}\n\n確定？"))
            return;
        CloseDialog();
        await RunJobAsync("提交中…", async () =>
        {
            var result = await GitHubService.CommitAsync(Catalog!.Root, message).ConfigureAwait(false);
            if (!push)
                return result;
            try
            {
                return result + "\n\n" + await GitHubService.PublishBranchAsync(Catalog!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return result + "\n\n提交成功，但發布失敗：\n" + ex.Message;
            }
        }).ConfigureAwait(false);
        await RefreshGitStatusAsync().ConfigureAwait(false);
    }

    public void OpenGithubDialog()
    {
        if (!RequireCatalog())
            return;
        _ = OpenGithubDialogAsync();
    }

    public void OpenDeployDialog()
    {
        if (!RequireCatalog())
            return;
        DeployDraft = DeployConfigResolver.Resolve(Catalog);
        DeploySaveTarget = "local";
        Dialog = "deploy";
        Notify();
    }

    public void OpenUatDialog()
    {
        if (!RequireCatalog())
            return;
        UatTitle = "";
        UatDescription = "";
        UatImages.Clear();
        Dialog = "uat";
        Notify();
    }

    public async Task AddUatFileAsync()
    {
        var files = await _native.PickFilesAsync("選擇截圖", ("圖片", [".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"])).ConfigureAwait(false);
        if (files is null)
            return;
        foreach (var f in files)
        {
            if (!UatImages.Contains(f))
                UatImages.Add(f);
        }
        Notify();
    }

    public void PasteUatClipboard()
    {
        var dest = Path.Combine(Path.GetTempPath(), $"uat-clip-{Guid.NewGuid():N}.png");
        var err = CursorLauncher.SaveClipboardImageWindows(dest);
        if (err is not null)
        {
            _native.Warn("剪貼簿", err);
            return;
        }
        UatImages.Add(dest);
        Notify();
    }

    public void RemoveUatImage(string path)
    {
        UatImages.Remove(path);
        Notify();
    }

    public async Task SubmitUatAsync()
    {
        if (!RequireCatalog() || Runtime is null)
            return;
        List<string> images = [];
        if (UatImages.Count > 0)
        {
            var (_, copied) = CursorLauncher.StageUatScreenshots(Runtime.UatReports, string.IsNullOrEmpty(UatTitle) ? "UAT 問題" : UatTitle, UatImages);
            images = copied;
        }
        var prompt = CursorLauncher.BuildUatAgentPrompt(Catalog!.Root, UatTitle, UatDescription, images);
        CloseDialog();
        await LaunchCursorNewAgentAsync(prompt, "UAT 求救已送出，請在 Cursor 跳出視窗按確認").ConfigureAwait(false);
    }

    public void OpenCompileHelp()
    {
        if (!RequireCatalog() || LastBuildFailure is null)
        {
            _native.Info("編譯求救", "目前沒有建置錯誤可送出。");
            return;
        }
        AgentPrompt = CursorLauncher.BuildAgentPrompt(
            Catalog!.Root, LastBuildFailure.Target, LastBuildFailure.ExitCode, LastBuildFailure.Log);
        AgentIntro = "確認後會開啟 Cursor 並跳出確認視窗；再按確認即建立 New Agent。錯誤內容會直接帶入提示，不會先寫求助檔。";
        Dialog = "agent";
        Notify();
    }

    public async Task ConfirmAgentAsync()
    {
        var prompt = AgentPrompt;
        CloseDialog();
        await LaunchCursorNewAgentAsync(prompt, "編譯求救已送出，請在 Cursor 跳出視窗按確認").ConfigureAwait(false);
    }

    public async Task SaveGithubAsync()
    {
        if (Catalog is null)
            return;
        if (!GithubDraft.IsComplete())
        {
            _native.Warn("設定未齊全", "請填寫 owner + repo，或提供 Remote URL。");
            return;
        }
        if (GithubSaveTarget == "manifest")
        {
            GithubConfigResolver.WriteManifest(Catalog, GithubDraft);
            Catalog = ServiceCatalogBuilder.Build(Catalog.Root);
        }
        else
            GithubConfigResolver.SaveLocal(Catalog.Root, GithubDraft);
        var msg = "已儲存 GitHub 設定";
        if (GithubApplyRemote)
            msg = await GithubConfigResolver.ApplyRemoteAsync(Catalog, GithubDraft).ConfigureAwait(false);
        CloseDialog();
        JobText = "已儲存 GitHub 設定";
        _native.Info("GitHub 設定", msg);
        Notify();
    }

    public void SaveDeploy()
    {
        if (Catalog is null)
            return;
        if (DeploySaveTarget == "manifest")
        {
            DeployConfigResolver.WriteManifest(Catalog, DeployDraft);
            Catalog = ServiceCatalogBuilder.Build(Catalog.Root);
        }
        else
            DeployConfigResolver.SaveLocal(Catalog.Root, DeployDraft);
        CloseDialog();
        JobText = $"已儲存部署設定（{DeployTargets.TargetLabel(DeployDraft.NormalizedTarget())}）";
        Notify();
    }

    public void CloseDialog()
    {
        Dialog = null;
        Notify();
    }

    public async Task ExitAsync()
    {
        var running = Catalog is null ? [] : Catalog.Services.Where(s => Health.GetValueOrDefault(s.Id)).Select(s => s.Label).ToList();
        var stopServices = false;
        if (running.Count > 0)
        {
            var listed = string.Join('\n', running.Take(10).Select(n => "· " + n));
            var more = running.Count > 10 ? $"\n…另有 {running.Count - 10} 個" : "";
            var ans = _native.YesNoCancel(
                "離開",
                $"警告：目前仍有 {running.Count} 個服務在執行中：\n{listed}{more}\n\n離開前要一併停止這些服務嗎？\n\n「是」＝停止服務後離開\n「否」＝保留服務繼續執行，仍離開\n「取消」＝不離開");
            if (ans == PhotinoDialogResult.Cancel)
                return;
            stopServices = ans == PhotinoDialogResult.Yes;
        }
        else if (!_native.Confirm("離開", "確定離開控制台？"))
            return;

        var closeIde = _native.Confirm("關閉 Cursor", "要一併關閉 Cursor 嗎？");
        if (stopServices && Catalog is not null && Runtime is not null)
        {
            try
            {
                ProcessSupervisor.StopAll(Catalog, Runtime);
            }
            catch (Exception ex)
            {
                if (!_native.Confirm("停止服務失敗", $"停止服務時發生問題：\n{ex.Message}\n\n仍要離開嗎？"))
                    return;
            }
        }
        if (closeIde)
        {
            var err = CursorLauncher.CloseCursor();
            if (err is not null && !_native.Confirm("關閉 Cursor", $"關閉 Cursor 時發生問題：\n{err}\n\n仍要離開控制台嗎？"))
                return;
        }
        _native.Close();
        await Task.CompletedTask;
    }

    public Task RefreshBuildStatesAsync()
    {
        var catalog = Catalog;
        if (catalog is null)
            return Task.CompletedTask;
        return Task.Run(() =>
        {
            var proj = BuildFreshness.AllProjectBuildStates(catalog);
            var svcStates = BuildFreshness.AllServiceBuildStates(catalog);
            var staleSvc = svcStates.Count(s => s.Status is "stale" or "unbuilt");
            var stalePrj = proj.Count(p => p.Status is "stale" or "unbuilt");
            Projects = proj;
            WarnText = staleSvc > 0 || stalePrj > 0
                ? $"{staleSvc} 個服務、{stalePrj} 個專案需重編（到「專案」頁編譯）"
                : "";
            Notify();
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task OpenGithubDialogAsync()
    {
        GithubDraft = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
        GithubSaveTarget = "local";
        GithubApplyRemote = true;
        Dialog = "github";
        Notify();
    }

    private async Task<bool> EnsureGithubAsync()
    {
        if (!RequireCatalog())
            return false;
        if ((await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false)).IsComplete())
            return true;
        await OpenGithubDialogAsync().ConfigureAwait(false);
        return false;
    }

    private async Task<bool> EnsureDeployAsync()
    {
        if (!RequireCatalog())
            return false;
        if (DeployConfigResolver.Resolve(Catalog).IsComplete())
            return true;
        await EditDeployAsync().ConfigureAwait(false);
        return false;
    }

    private Task EditDeployAsync()
    {
        OpenDeployDialog();
        return Task.CompletedTask;
    }

    private Task EditGithubAsync() => OpenGithubDialogAsync();

    private async Task OpenReleaseDialogAsync()
    {
        if (!RequireCatalog())
            return;
        if (!GitHubService.GhAvailable())
        {
            _native.Error("需要 GitHub CLI", "發行 Release 需要 gh。請安裝：https://cli.github.com/ 並執行 gh auth login。");
            return;
        }
        ReleaseInspect? inspect = null;
        await RunJobAsync("讀取 Release…", async () =>
        {
            inspect = await GitHubService.InspectReleaseAsync(Catalog!).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (inspect is null)
            return;
        ReleaseTag = inspect.SuggestedTag;
        ReleaseTitle = inspect.SuggestedTag;
        ReleaseNotes = "";
        ReleaseTarget = inspect.CurrentBranch;
        ReleaseDraft = false;
        ReleasePrerelease = false;
        ReleaseGenerateNotes = true;
        ReleaseMakeLatest = true;
        ReleaseAssets.Clear();
        ReleaseHint = inspect.Summary;
        ReleaseLatestTag = inspect.LatestGithubTag;
        Dialog = "release";
        Notify();
    }

    private async Task RunBuildActionAsync(string handler)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        RightTab = "build";
        BuildText = "";
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Notify();
        await RunJobAsync("建置中…", async () =>
        {
            var targets = BuildRunner.TargetsFor(catalog, handler);
            if (targets.Count == 0)
            {
                AppendBuild("沒有需要編譯的項目。");
                return (string?)null;
            }
            var allLines = new List<string>();
            string? failedTarget = null;
            var failedCode = 0;
            foreach (var target in targets)
            {
                var header = $"=== build {target} ===";
                allLines.Add(header);
                AppendBuild(header);
                var progress = new Progress<string>(line =>
                {
                    allLines.Add(line);
                    AppendBuild(line);
                });
                var (code, _) = await BuildRunner.BuildAsync(catalog.Root, target, progress).ConfigureAwait(false);
                var footer = $"exit {code}  ({target})";
                allLines.Add(footer);
                AppendBuild(footer);
                if (code != 0 && failedTarget is null)
                {
                    failedTarget = target;
                    failedCode = code;
                }
            }
            if (failedTarget is not null)
                RememberBuildFailure(failedTarget, failedCode, string.Join('\n', allLines));
            return (string?)null;
        }).ConfigureAwait(false);
    }

    private async Task LaunchCursorNewAgentAsync(string prompt, string status)
    {
        if (Catalog is null)
            return;
        try
        {
            if (Js is not null)
                await Js.InvokeVoidAsync("aiConsole.copyText", prompt).ConfigureAwait(false);
        }
        catch
        {
            // clipboard optional
        }
        var err = CursorLauncher.OpenProjectForNewAgent(Catalog.Root);
        if (err is not null)
        {
            _native.Warn("Cursor", err);
            return;
        }
        await Task.Delay(CursorLauncher.NewAgentLaunchDelayMs()).ConfigureAwait(false);
        var err2 = CursorLauncher.OpenPromptDeeplink(prompt);
        if (err2 is not null)
        {
            _native.Warn("Cursor", err2);
            return;
        }
        JobText = status;
        Notify();
    }

    private void RememberBuildFailure(string target, int exitCode, string log)
    {
        LastBuildFailure = new BuildFailure(target, exitCode, log);
        CompileHelpEnabled = true;
        AppendBuild("建置失敗 — 可點「編譯求救」開啟 New Agent");
        Notify();
    }

    private void AppendBuild(string line)
    {
        BuildText += line + "\n";
        Notify();
    }

    private ServiceEntry? SelectedService()
    {
        if (Catalog is null || SelectedServiceId is null)
            return null;
        return ServiceCatalogBuilder.ById(Catalog, SelectedServiceId);
    }

    private void AppendLogTail(bool full = false)
    {
        if (Catalog is null || Runtime is null)
            return;
        var svc = SelectedService();
        if (svc is null)
            return;
        var path = Runtime.LogPath(ServiceCatalogBuilder.HostService(Catalog, svc).Stem);
        if (!File.Exists(path))
            return;
        byte[] data;
        try { data = LogFileUtil.ReadAllBytes(path); }
        catch { return; }
        string chunk;
        if (full)
        {
            var lines = Encoding.UTF8.GetString(data).Replace("\r\n", "\n").Split('\n');
            var tail = lines.Length > 500 ? lines[^500..] : lines;
            _logOffset = data.Length;
            chunk = string.Join('\n', tail) + (tail.Length > 0 ? "\n" : "");
        }
        else
        {
            if (data.Length <= _logOffset)
                return;
            chunk = Encoding.UTF8.GetString(data, (int)_logOffset, data.Length - (int)_logOffset);
            _logOffset = data.Length;
        }
        if (chunk.Length == 0)
            return;
        LogText += chunk;
        if (LogText.Length > 400_000)
            LogText = LogText[^300_000..];
    }

    private bool RequireCatalog()
    {
        if (Catalog is not null && Runtime is not null)
            return true;
        _native.Info("尚未選擇專案", "請先選擇專案目錄。");
        return false;
    }

    private async Task RunJobAsync(string title, Func<Task<string?>> fn)
    {
        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return;
        }
        JobBusy = true;
        JobText = title;
        Notify();
        string? err = null;
        string? msg = null;
        try
        {
            msg = await Task.Run(fn).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            err = ex.Message;
        }
        JobBusy = false;
        JobText = err is null ? "完成" : "錯誤";
        Notify();
        if (err is not null)
            _native.Error(title, err);
        else if (!string.IsNullOrWhiteSpace(msg))
            _native.Info(title, msg);
        UpdateReady();
        await RefreshBuildStatesAsync().ConfigureAwait(false);
    }

    private async Task PollLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(800));
        var healthEvery = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                if (FollowLog)
                    AppendLogTail();
                healthEvery++;
                if (healthEvery % 3 == 0 && Catalog is not null)
                {
                    var catalog = Catalog;
                    var health = new Dictionary<string, bool>();
                    foreach (var svc in catalog.Services)
                        health[svc.Id] = await ProcessSupervisor.ProbeHealthAsync(svc).ConfigureAwait(false);
                    Health.Clear();
                    foreach (var kv in health)
                        Health[kv.Key] = kv.Value;
                    UpdateReady();
                }
                if (healthEvery % 8 == 0 && Catalog is not null)
                    await RefreshGitStatusAsync().ConfigureAwait(false);
                Notify();
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private void ResetToStartup()
    {
        Catalog = null;
        Runtime = null;
        SelectedServiceId = null;
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Health.Clear();
        StartErrors.Clear();
        Projects = [];
        WarnText = "";
        GitStatusText = "";
        GitBrief = null;
        CommitMessage = "";
        CommitHint = "";
        CommitSuggestHint = "";
        CommitPushAfter = false;
        CommitChanges = [];
        LogFilter = "";
        LogTitle = "Log · （未選服務）";
        LogText = "";
        BuildText = "";
        ReadyText = "就緒 0 / 0";
        JobText = "待命";
        JobBusy = false;
        _logOffset = 0;
        LeftTab = "svc";
        RightTab = "log";
        Dialog = null;
        GithubDraft = new();
        GithubSaveTarget = "local";
        GithubApplyRemote = true;
        DeployDraft = new();
        DeploySaveTarget = "local";
        UatTitle = "";
        UatDescription = "";
        UatImages.Clear();
        ConfirmTitle = "";
        ConfirmBody = "";
        ConfirmAction = null;
        AgentPrompt = "";
        AgentIntro = "";
        ReleaseTag = "";
        ReleaseTitle = "";
        ReleaseNotes = "";
        ReleaseTarget = "";
        ReleaseHint = "";
        ReleaseLatestTag = "";
        ReleaseBasisTag = "";
        ReleaseDraft = false;
        ReleasePrerelease = false;
        ReleaseGenerateNotes = true;
        ReleaseMakeLatest = true;
        ReleaseAssets.Clear();
    }

    private async Task RestoreLastProjectOnStartAsync()
    {
        if (!RestoreLastProject)
            return;
        var path = ConsoleSettingsStore.LastProject();
        if (string.IsNullOrEmpty(path))
            return;
        await LoadProjectAsync(path, openCursor: false).ConfigureAwait(false);
        if (Catalog is not null && !JobBusy)
        {
            JobText = "已還原上次專案";
            Notify();
        }
    }

    private async Task RefreshGitStatusAsync()
    {
        var root = Catalog?.Root;
        if (string.IsNullOrEmpty(root))
        {
            GitBrief = null;
            GitStatusText = "";
            return;
        }
        try
        {
            var brief = await GitHubService.TryBriefStatusAsync(root).ConfigureAwait(false);
            GitBrief = brief;
            GitStatusText = brief?.Format() ?? "";
        }
        catch
        {
            GitBrief = null;
            GitStatusText = "";
        }
    }

    private void ApplyStartResults(IReadOnlyList<(string Id, string Label, string? Error)> results)
    {
        if (results.Count == 0)
            return;
        foreach (var (id, _, error) in results)
        {
            if (error is null)
                StartErrors.Remove(id);
            else
                StartErrors[id] = error;
        }
        var failed = results.Where(r => r.Error is not null).ToList();
        if (failed.Count > 0)
            WarnText = string.Join("；", failed.Select(f => $"{f.Label}：{f.Error}"));
        else if (StartErrors.Count == 0)
            WarnText = "";
    }

    private void UpdateReady()
    {
        ReadyText = $"就緒 {ReadyCount} / {ServiceCount}";
        if (!JobBusy)
            JobText = Catalog is null ? "待命" : JobText;
    }

    private void Notify() => Changed?.Invoke();
}
