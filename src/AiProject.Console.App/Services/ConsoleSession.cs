using System.Text;
using AiProject.Console.Core;
using AiProject.Console.Core.Actions;
using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Build;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Stack;
using AiProject.Console.Core.Tech;
using AiProject.Console.Core.Update;
using AiProject.Console.Core.Util;
using AiProject.Console.Core.WorkHours;
using AiProject.Console.CompanyClient;
using Microsoft.JSInterop;
using Photino.NET;

namespace AiProject.Console.App.Services;

public sealed partial class ConsoleSession : IDisposable
{
    private readonly NativeUi _native;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _askCts;
    private CancellationTokenSource? _askProbeCts;
    private CancellationTokenSource? _githubLoginCts;
    private bool _pendingOpenCursor;
    private bool _unassignedCollapseUserSet;
    private bool _autoSyncSkippedDirty;
    private const string AutoSyncSkippedDirtyText = "工作區不乾淨，已略過自動同步";
    private long _logOffset;
    private DateTimeOffset? _ciWatchUntil;
    private int _ciWatchGen;
    private DocsServeHandle? _docsServe;
    private readonly WorkHoursStore _workHours = new();
    private readonly ExecutionStatusChartStore _statusChart = new();
    private readonly ICompanyPlatformClient? _company;
    private readonly Dictionary<string, string?> _iconUrlCache = new(StringComparer.OrdinalIgnoreCase);
    private int _timesheetGen;
    private TaskCompletionSource<bool>? _leaveGateTcs;
    private string? _releaseReturnDialog;

    public ConsoleSession(NativeUi native, ICompanyPlatformClient? company = null)
    {
        _native = native;
        _company = company;
        OpenWithCursor = ConsoleSettingsStore.GetOpenIdeOnLoad();
        RestoreLastProject = ConsoleSettingsStore.GetRestoreLastProject();
        TestBeforePush = ConsoleSettingsStore.GetTestBeforePush();
        Theme = ConsoleSettingsStore.GetTheme();
        AgentProvider = ConsoleSettingsStore.GetAgentProvider();
        AgentCliPath = ConsoleSettingsStore.GetAgentCliPath();
        CustomAgentCommand = ConsoleSettingsStore.GetCustomAgentCommand();
        CustomAgentArgs = ConsoleSettingsStore.GetCustomAgentArgs();
        AskBaseUrl = ConsoleSettingsStore.GetAskBaseUrl();
        AskApiKey = ConsoleSettingsStore.GetAskApiKey();
        AskModel = ConsoleSettingsStore.GetAskModel();
        AskSources = ConsoleSettingsStore.GetAskSources();
        Workbench = ConsoleSettingsStore.GetWorkbench();
        GitHostName = ConsoleSettingsStore.GetGitHost();
        GitKind = ConsoleSettingsStore.GetGitKind();
        CompanyBaseUrl = ConsoleSettingsStore.GetCompanyBaseUrl();
        RefreshAgentDetect();
        _ = PollLoopAsync();
        _ = CheckUpdateOnStartAsync();
        _ = RefreshGithubAuthAsync();
        _ = RestoreLastProjectOnStartAsync();
    }

    public IJSRuntime? Js { get; set; }

    public event Action? Changed;

    public ProjectCatalog? Catalog { get; private set; }
    public ProjectRuntime? Runtime { get; private set; }
    public string? WorkspaceAppIconUrl => AppIconDataUrl(Catalog is null ? null : AppIconLocator.WorkspaceIcon(Catalog));
    public IReadOnlyList<string> RecentProjects => ConsoleSettingsStore.RecentProjects();
    public IReadOnlyList<string> HistoryProjects => ConsoleSettingsStore.HistoryProjects(Catalog?.Root);
    public bool OpenWithCursor { get; set; }
    public bool RestoreLastProject { get; set; }
    public bool TestBeforePush { get; set; }
    public string Theme { get; set; } = "light";
    public string AgentProvider { get; set; } = "cursor";
    public string AgentCliPath { get; set; } = "";
    public string CustomAgentCommand { get; set; } = "";
    public string CustomAgentArgs { get; set; } = "";
    public bool McpReadOnly { get; set; }
    public string McpAllow { get; set; } = "";
    public string McpDeny { get; set; } = "";
    public string McpConfirm { get; set; } = "stop_all";
    public IReadOnlyList<ProjectMcpServer> ProjectMcpServers { get; private set; } = [];
    public bool ProjectHasMcpPolicy { get; private set; }
    public string AddToolName { get; set; } = "";
    public string AddMcpId { get; private set; } = McpLaunch.ServerId;
    public string CustomMcpId { get; set; } = "";
    public string CustomMcpKind { get; set; } = "command";
    public string CustomMcpCommand { get; set; } = "npx";
    public string CustomMcpArgs { get; set; } = "";
    public string CustomMcpUrl { get; set; } = "";
    public string AskBaseUrl { get; set; } = ProjectAskService.DefaultBaseUrl;
    public string AskApiKey { get; set; } = "";
    public string AskModel { get; set; } = ProjectAskService.DefaultModel;
    public IReadOnlyList<ProjectAskSource> AskSources { get; private set; } = [];
    public string AskDraft { get; set; } = "";
    public bool AskBusy { get; private set; }
    public string AskStatus { get; private set; } = "";
    public bool AskProbeBusy { get; private set; }
    public string AskProbeMessage { get; private set; } = "";
    public bool AskProbeOk { get; private set; }
    public bool AskProbeModelFound { get; private set; }
    public IReadOnlyList<string> AskProbeModels { get; private set; } = [];
    public IReadOnlyList<ProjectAskChatItem> AskMessages { get; private set; } = [];
    public string AgentDetectSummary { get; private set; } = "";
    public bool AgentAvailable { get; private set; }
    public string ReadyText { get; private set; } = "就緒 0 / 0";
    public string JobText { get; private set; } = "待命";
    public int BuildDone { get; private set; }
    public int BuildTotal { get; private set; }
    public int BuildFailedCount { get; private set; }
    public string BuildCurrentName { get; private set; } = "";
    public string BuildProgressText =>
        BuildTotal <= 0
            ? ""
            : BuildDone >= BuildTotal
                ? $"{BuildDone}/{BuildTotal} 完成" + (BuildFailedCount > 0 ? $"（{BuildFailedCount} 失敗）" : "")
                : $"{BuildDone}/{BuildTotal}";
    public string WarnText { get; private set; } = "";
    public string GitStatusText { get; private set; } = "";
    public string LogFilter { get; private set; } = "";
    public Dictionary<string, string> StartErrors { get; } = new();
    public Dictionary<string, string> ServiceActivities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool JobBusy { get; private set; }
    public string LeftTab { get; set; } = "svc";
    public string RightTab => LeftTab switch
    {
        "prj" => "build",
        "docs" => "docs",
        _ => "log"
    };
    public bool AskPanelOpen { get; private set; }
    public bool AuditPanelOpen { get; private set; }
    public string PrefsTab { get; set; } = "general";
    public string Workbench { get; private set; } = "dev";
    public bool IsReqWorkbench => Workbench == "req";
    public string GitHostName { get; set; } = GitHost.PublicHostname;
    public string GitKind { get; set; } = GitHost.KindGithub;
    public string CompanyBaseUrl { get; set; } = "";
    public IReadOnlyList<string> RecentGitHosts => ConsoleSettingsStore.RecentGitHosts();
    public string ActiveGitHost =>
        !string.IsNullOrWhiteSpace(GithubDraft.Host) ? GitHost.Normalize(GithubDraft.Host)
        : GitHost.Normalize(GitHostName);
    public string GithubAccountText => GithubAccount.Display();
    public string GithubAccountLabel => GithubLoggedIn ? GithubAccountText : "未登入";
    public bool GithubNeedsAttention =>
        HasUncommitted || GitPulseBlocked || GitBrief is { Behind: > 0 };
    public string GithubStatusTone => GithubAccountStatus.Tone(GithubLoggedIn, GithubNeedsAttention);
    public string GithubStatusTitle => GithubAccountStatus.Title(GithubLoggedIn, GitBrief);
    public bool GitIssuesReady => GitHost.IssuesReady(GitKind);
    public WorkHoursView WorkHoursView { get; private set; } = WorkHoursView.Week;
    public DateOnly WorkHoursAnchor { get; private set; } = DateOnly.FromDateTime(DateTime.Now);
    public string? WorkHoursProjectFilter { get; private set; }
    public string WorkHoursPane { get; private set; } = "dash";
    public WorkTimesheet? WorkTimesheet { get; private set; }
    public bool WorkHoursTimesheetBusy { get; private set; }
    public string WorkHoursTimesheetHint { get; private set; } = "";
    public bool WorkHoursExporting { get; private set; }
    public TimeSpan SessionWorkDuration => _workHours.CurrentDuration();
    public TimeSpan TodayWorkDuration => _workHours.TodayDuration();
    public string SessionWorkText => WorkHoursFormat.Compact(SessionWorkDuration);
    public string TodayWorkText => WorkHoursFormat.Duration(TodayWorkDuration);
    public string WorkHoursTone => WorkHoursFormat.Tone(TodayWorkDuration);
    public IReadOnlyList<WorkHoursProjectSummary> WorkHoursProjects
    {
        get
        {
            var (start, end) = WorkHoursAggregator.Range(WorkHoursView, WorkHoursAnchor);
            return WorkHoursAggregator.SummarizeProjects(_workHours.VisibleSessions, start, end, DateTimeOffset.Now);
        }
    }
    public WorkHoursReport WorkHoursReport
    {
        get
        {
            var sessions = WorkHoursAggregator.FilterProject(_workHours.VisibleSessions, WorkHoursProjectFilter);
            return WorkHoursAggregator.Build(sessions, WorkHoursView, WorkHoursAnchor, DateTimeOffset.Now);
        }
    }
    public string WorkHoursPersonLabel => _workHours.PersonLabel;
    public string WorkHoursPersonKey => _workHours.PersonKey;
    public string? SelectedServiceId { get; private set; }
    public Dictionary<string, bool> Health { get; } = new();
    public IReadOnlyList<BuildState> Projects { get; private set; } = [];
    public bool StaleOnly { get; set; }
    public bool FollowLog { get; set; } = true;
    public string LogTitle { get; private set; } = "Log · （未選服務）";
    public string LogText { get; private set; } = "";
    public string VisibleLogText => TextFilter.Apply(LogText, LogFilter);
    public string BuildText { get; private set; } = "";
    public IReadOnlyList<McpAuditEntry> AuditEntries { get; private set; } = [];
    public int AuditTotal { get; private set; }
    public string AuditPolicyText { get; private set; } = "";
    public string AuditFilter { get; private set; } = "";
    public int AuditFailCount => AuditEntries.Count(e => !e.Ok);
    public int AuditIncidentCount => AuditEntries.Count(e => e.IsIncident);
    public IEnumerable<McpAuditEntry> VisibleAuditEntries
    {
        get
        {
            var q = AuditFilter.Trim();
            var newest = AuditEntries.Reverse();
            if (string.IsNullOrEmpty(q))
                return newest;
            return newest.Where(e =>
                e.Tool.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.Error?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.ArgsText.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.StatusText.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
    }
    public bool CompileHelpEnabled { get; private set; }
    public BuildFailure? LastBuildFailure { get; private set; }

    public string? Dialog { get; private set; }
    public string LeaveGateAction { get; private set; } = "";
    public string? LeaveGateReason { get; private set; }
    public string LeaveGateTitle => GitBriefStatus.LeaveGateTitle(string.IsNullOrEmpty(LeaveGateAction) ? "離開" : LeaveGateAction);
    public string LeaveGateForceLabel => GitBriefStatus.LeaveGateForceLabel(LeaveGateAction);
    public DoctorSnapshot? DoctorView { get; private set; }
    public bool DoctorCopied { get; private set; }
    public ReleaseListView? ReleaseList { get; private set; }
    public InfoReport? InfoReport { get; private set; }
    public JobResultView? JobResult { get; private set; }
    public ReleaseRunState? ReleaseRun { get; private set; }
    public bool ReleaseProgressReturnsToForm => _releaseReturnDialog == "release";
    public bool InfoCopied { get; private set; }
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
    public string AgentTitle { get; private set; } = "編譯求救";
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
    public bool ReleasePackable { get; private set; }
    public bool ReleaseHasSetup => ConsoleReleasePack.HasSetupAsset(ReleaseAssets);
    public string ReleasePackHint =>
        !ReleasePackable
            ? ""
            : ReleaseHasSetup
                ? "已附上 *-win-x64-setup.exe。已安裝使用者按「立即更新」會啟動安裝程式。"
                : ReleaseDraft
                    ? "草稿可不附安裝包。正式發行前請打包，否則自動更新會改開 GitHub 頁。"
                    : "尚未附加 *-win-x64-setup.exe。按「發行」會先打包再上傳，畫面會顯示步驟與紀錄（編譯可能要 1–3 分鐘）。沒有這個檔，自動更新只能開 GitHub 頁。";
    public string CommitMessage { get; set; } = "";
    public string CommitSubject { get; set; } = "";
    public string CommitBody { get; set; } = "";
    public string CombinedCommitMessage => CommitMessageSuggester.CombineMessage(CommitSubject, CommitBody);
    public string CommitHint { get; private set; } = "";
    public bool CommitPushAfter { get; set; }
    public IReadOnlyList<GitChange> CommitChanges { get; private set; } = [];
    public HashSet<string> CommitSelected { get; } = new(StringComparer.Ordinal);
    public string CommitFileQuery { get; set; } = "";
    public IReadOnlyList<GitChange> VisibleCommitChanges
    {
        get
        {
            var q = CommitFileQuery.Trim();
            if (string.IsNullOrEmpty(q))
                return CommitChanges;
            return [.. CommitChanges.Where(c =>
                c.Path.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.OriginalPath?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || c.KindLabel.Contains(q, StringComparison.OrdinalIgnoreCase))];
        }
    }
    public IReadOnlyList<GitChange> SelectedCommitChanges =>
        [.. CommitChanges.Where(c => CommitSelected.Contains(c.Path))];
    public string? CommitPreviewPath { get; private set; }
    public string CommitPreviewDiff { get; private set; } = "";
    public bool CommitPreviewBusy { get; private set; }
    public string CommitSuggestHint { get; private set; } = "";
    public GitBriefStatus? GitBrief { get; private set; }
    public bool HasUncommitted => GitBrief is { DirtyCount: > 0 };
    public IReadOnlyList<GitBranchInfo> BranchList { get; private set; } = [];
    public string NewBranchName { get; set; } = "";
    public string BranchDialogHint { get; private set; } = "";
    public AvailableUpdate? UpdateAvailable { get; private set; }
    public GithubAccount GithubAccount { get; private set; } = GithubAccount.None;
    public bool GithubLoggedIn => GithubAccount.LoggedIn;
    public bool GithubManaged { get; private set; }
    public ActionsSnapshot? Actions { get; private set; }
    public bool ShowCiChip => HasProject && GithubManaged && GithubLoggedIn;
    public string CiChipText => Actions?.ChipText() ?? "CI …";
    public bool WatchingCi => _ciWatchUntil is { } until && DateTimeOffset.UtcNow < until;
    public string CiChipTone => ActionsSnapshot.ConsoleChipTone(Actions?.ChipTone() ?? "wait", WatchingCi);
    public bool CanScaffoldCi => Catalog is not null && !CiWorkflow.HasBuildTest(Catalog.Root);
    public PullRequestStatus? PullRequest { get; private set; }
    public bool ShowPrChip =>
        ShowCiChip
        && PullRequest is not null
        && (PullRequest.HasPr
            || !string.IsNullOrEmpty(PullRequest.Error)
            || (GitBrief is { Branch: var branch }
                && branch is not "main" and not "master" and not "HEAD"));
    public string PrChipText => PullRequest?.ChipText() ?? "PR …";
    public string PrChipTone => PullRequest?.ChipTone() ?? "wait";
    public bool GithubAuthBusy { get; private set; }
    public string GithubAuthHint { get; private set; } = "";
    public string GithubLoginCode { get; private set; } = "";
    public string GithubLoginUrl { get; private set; } = "";
    public bool GithubLoginBrowserOpened { get; private set; }
    public bool GithubLoginCodeCopied { get; private set; }
    public IReadOnlyList<GithubIssue> AssignedIssues { get; private set; } = [];
    public IReadOnlyList<GithubIssue> UnassignedIssues { get; private set; } = [];
    public string IssuesHint { get; private set; } = "";
    public bool IssuesBusy { get; private set; }
    public bool UnassignedCollapsed { get; private set; } = true;

    public IReadOnlyList<ConsoleAction> BuildActions => ActionCatalog.Load("build");
    public IReadOnlyList<ConsoleAction> GithubActions => ActionCatalog.Load("github");
    public IReadOnlyList<ConsoleAction> DeployActions => ActionCatalog.Load("deploy");
    public IReadOnlyList<ConsoleAction> DocsActions => ActionCatalog.Load("docs");
    public IEnumerable<ConsoleAction> GithubLane(string lane) =>
        GithubActions.Where(a => string.Equals(a.Lane, lane, StringComparison.OrdinalIgnoreCase));
    public bool GithubHubOpen { get; private set; }
    public bool ShowGitPulse => HasProject;
    public bool GitPulseBlocked => GitBrief is { } brief && !brief.IsClearToLeave;
    public GitHubNextStep GithubNext => GitHubNextAction.Decide(
        GitBrief,
        PullRequest?.HasPr ?? false,
        string.IsNullOrWhiteSpace(GithubDraft.DefaultBranch) ? "main" : GithubDraft.DefaultBranch,
        Actions?.Latest?.IsInProgress == true,
        WatchingCi && Actions?.Latest?.IsFailure == true);
    public string GithubRepoText
    {
        get
        {
            var slug = GithubDraft.Slug();
            if (!string.IsNullOrEmpty(slug))
                return slug;
            return HasProject ? (Catalog?.Name ?? "未設定遠端") : "尚未選擇專案";
        }
    }
    public string GithubHostText => GithubDraft.ResolvedHost();
    public DocsStatus? Docs { get; private set; }
    public string? SelectedDocPath { get; private set; }
    public string DocsDraft { get; set; } = "";
    public bool DocsDirty { get; private set; }
    public bool DocsPreviewMode { get; private set; }
    public string DocsHint { get; private set; } = "";
    public bool DocsServing => _docsServe is { IsRunning: true };
    public string DocsServeUrl => _docsServe?.Url ?? DocsService.DefaultServeUrl;
    public string DocsPreviewHtml => DocsMarkdown.ToSafeHtml(DocsDraft);
    public string DocsPreviewPageUrl =>
        Catalog is null
            ? DocsServeUrl
            : DocsService.PreviewUrl(Catalog.Root, SelectedDocPath, DocsServeUrl);
    public bool DocsPreviewOpensCurrentPage =>
        !string.Equals(DocsPreviewPageUrl.TrimEnd('/'), DocsServeUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
    public bool DocsChipWarn => Docs is null or { Health: not DocsHealth.Ready };
    public string NewDocPath { get; private set; } = "";
    public string NewDocHint { get; private set; } = "";

    private readonly HashSet<string> _collapsedServiceGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedProjectGroups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedDocFolders = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ServiceGroupNode> ServiceGroupRoots =>
        Catalog is null ? [] : ServiceGroupTree.Build(Catalog.Services);

    public IEnumerable<ServiceGroupNode> VisibleServiceGroupNodes() =>
        ServiceGroupTree.WalkVisible(ServiceGroupRoots, IsServiceGroupCollapsed);

    public ServiceActionFlags ActionsFor(ServiceEntry svc)
    {
        var self = IsSelfService(svc);
        return ServiceActionPolicy.ForRow(svc, self || Health.GetValueOrDefault(svc.Id), self);
    }

    public ServiceActionFlags ActionsFor(ServiceGroupNode node) =>
        ServiceActionPolicy.ForGroup(node.Descendants().Select(s =>
        {
            var self = IsSelfService(s);
            return (s, self || Health.GetValueOrDefault(s.Id), self);
        }));

    public IReadOnlyList<IAgentBackend> AgentBackends => AgentBackendRegistry.All;

    public IAgentBackend CurrentAgent => AgentBackendRegistry.Get(AgentProvider);

    public string AgentDisplayName => CurrentAgent.DisplayName;

    public IEnumerable<BuildState> VisibleProjects =>
        StaleOnly
            ? Projects.Where(p => p.Status is "stale" or "unbuilt" || p.Activity is "queued" or "building" or "failed")
            : Projects;

    public IEnumerable<IGrouping<string, BuildState>> ProjectGroups =>
        VisibleProjects.GroupBy(p => string.IsNullOrEmpty(p.System) ? "其他" : p.System);

    public string? AppIconDataUrl(string? relPath)
    {
        if (Catalog is null || string.IsNullOrWhiteSpace(relPath))
            return null;
        var full = AppIconLocator.ResolveAbsolute(Catalog.Root, relPath);
        if (full is null)
            return null;
        if (_iconUrlCache.TryGetValue(full, out var cached))
            return cached;
        var url = AppIconLocator.TryDataUrl(full);
        _iconUrlCache[full] = url;
        return url;
    }

    public bool IsServiceGroupCollapsed(string key) => _collapsedServiceGroups.Contains(key);

    public bool IsProjectGroupCollapsed(string key) => _collapsedProjectGroups.Contains(key);

    public void ToggleServiceGroup(string key)
    {
        if (!_collapsedServiceGroups.Add(key))
            _collapsedServiceGroups.Remove(key);
        Notify();
    }

    public void ToggleProjectGroup(string key)
    {
        if (!_collapsedProjectGroups.Add(key))
            _collapsedProjectGroups.Remove(key);
        Notify();
    }

    public void ToggleAllServiceGroups()
    {
        ToggleAllGroups(_collapsedServiceGroups, ServiceGroupTree.AllKeys(ServiceGroupRoots));
    }

    public IReadOnlyList<DocsTreeRow> VisibleDocsTree =>
        Docs is null ? [] : DocsService.FlattenTree(DocsService.BuildTree(Docs.Files), _collapsedDocFolders);

    public bool IsDocFolderCollapsed(string relPath) => _collapsedDocFolders.Contains(relPath);

    public void ToggleDocFolder(string relPath)
    {
        if (!_collapsedDocFolders.Add(relPath))
            _collapsedDocFolders.Remove(relPath);
        Notify();
    }

    public void SetDocsPreviewMode(bool value)
    {
        DocsPreviewMode = value;
        Notify();
    }

    public void SetNewDocPath(string value)
    {
        NewDocPath = value ?? "";
        Notify();
    }

    public void ToggleAllProjectGroups()
    {
        ToggleAllGroups(_collapsedProjectGroups, ProjectGroups.Select(g => g.Key));
    }

    public void ToggleUnassignedIssues()
    {
        _unassignedCollapseUserSet = true;
        UnassignedCollapsed = !UnassignedCollapsed;
        Notify();
    }

    private void ToggleAllGroups(HashSet<string> collapsed, IEnumerable<string> keys)
    {
        var list = keys.ToList();
        if (list.Count == 0)
            return;
        var allCollapsed = list.All(collapsed.Contains);
        collapsed.Clear();
        if (!allCollapsed)
        {
            foreach (var key in list)
                collapsed.Add(key);
        }
        Notify();
    }

    public int ReadyCount => Catalog is null ? 0 : Catalog.Services.Count(s => Health.GetValueOrDefault(s.Id));
    public int ServiceCount => Catalog?.Services.Count ?? 0;
    public bool HasOpenableFrontend => ServiceCatalogBuilder.HasOpenableFrontend(Catalog);
    public bool HasUatTarget => ServiceCatalogBuilder.HasUiOrService(Catalog);

    public bool IsSelfService(ServiceEntry svc) =>
        Catalog is not null && ServiceCatalogBuilder.IsCurrentConsole(Catalog, svc);

    public bool HasDepends(ServiceEntry svc) => svc.Dependencies.Count > 0;

    public string DependsHint(ServiceEntry svc) =>
        string.Join("、", svc.Dependencies.Select(d => d.Optional ? d.Id + "（可略過）" : d.Id));
    public int OfflineCount => Math.Max(0, ServiceCount - ReadyCount);
    public int StaleProjectCount => Projects.Count(p => p.Status is "stale" or "unbuilt");
    public string? LastAuditFailTool => AuditEntries.LastOrDefault(e => !e.Ok)?.Tool;
    public string? LastAuditIncidentTool => AuditEntries.LastOrDefault(e => e.IsIncident)?.Tool;
    public string DutyAttention =>
        Catalog is null
            ? ""
            : DutySummary.Attention(OfflineCount, StaleProjectCount, AuditIncidentCount, LastAuditIncidentTool);
    public bool DutyOk => Catalog is not null && DutySummary.IsClear(OfflineCount, StaleProjectCount, AuditIncidentCount);
    public bool AskConfigured => ProjectAskService.IsConfigured(AskBaseUrl, AskModel);
    public bool CanAsk => HasProject && AskConfigured && !AskBusy;
    public string AskProviderId => ProjectAskProviders.MatchId(AskBaseUrl);
    public string AskActiveSourceId =>
        AskSources.FirstOrDefault(s =>
            string.Equals(
                ProjectAskProviders.NormalizeUrl(s.BaseUrl),
                ProjectAskProviders.NormalizeUrl(AskBaseUrl),
                StringComparison.OrdinalIgnoreCase))?.Id ?? "";
    public IReadOnlyList<string> AskModelChoices
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            void Add(string? value)
            {
                var t = (value ?? "").Trim();
                if (string.IsNullOrEmpty(t) || !seen.Add(t))
                    return;
                list.Add(t);
            }
            Add(AskModel);
            foreach (var m in AskProbeModels)
                Add(m);
            foreach (var m in ProjectAskProviders.Get(AskProviderId).SuggestedModels)
                Add(m);
            return list;
        }
    }
    public string AskProbeTone =>
        string.IsNullOrEmpty(AskProbeMessage) || AskProbeBusy ? "" :
        !AskProbeOk ? "is-fail" :
        AskProbeModelFound || AskProbeModels.Count == 0 ? "is-ok" : "is-wait";
    public IReadOnlyList<ProjectAskSuggestionView> AskSuggestions =>
        ProjectAskPrompts.Rank(OfflineCount, StaleProjectCount, AuditIncidentCount);

    public bool RuntimeHelpEnabled
    {
        get
        {
            var svc = SelectedService();
            if (svc is null)
                return false;
            if (StartErrorFor(svc) is not null)
                return true;
            return CursorLauncher.HasRuntimeErrors(LogText);
        }
    }

    public void SetOpenWithCursor(bool value)
    {
        OpenWithCursor = value;
        ConsoleSettingsStore.SetOpenIdeOnLoad(value);
        Notify();
    }

    public void OpenPreferences(string? tab = null)
    {
        AgentProvider = ConsoleSettingsStore.GetAgentProvider();
        AgentCliPath = ConsoleSettingsStore.GetAgentCliPath();
        CustomAgentCommand = ConsoleSettingsStore.GetCustomAgentCommand();
        CustomAgentArgs = ConsoleSettingsStore.GetCustomAgentArgs();
        AskBaseUrl = ConsoleSettingsStore.GetAskBaseUrl();
        AskApiKey = ConsoleSettingsStore.GetAskApiKey();
        AskModel = ConsoleSettingsStore.GetAskModel();
        AskSources = ConsoleSettingsStore.GetAskSources();
        ClearAskProbe();
        Theme = ConsoleSettingsStore.GetTheme();
        RestoreLastProject = ConsoleSettingsStore.GetRestoreLastProject();
        TestBeforePush = ConsoleSettingsStore.GetTestBeforePush();
        OpenWithCursor = ConsoleSettingsStore.GetOpenIdeOnLoad();
        GitHostName = ConsoleSettingsStore.GetGitHost();
        GitKind = ConsoleSettingsStore.GetGitKind();
        McpReadOnly = ConsoleSettingsStore.GetMcpReadOnly();
        McpAllow = ConsoleSettingsStore.GetMcpAllow();
        McpDeny = ConsoleSettingsStore.GetMcpDeny();
        McpConfirm = ConsoleSettingsStore.GetMcpConfirm();
        RefreshMcpPrefsUi();
        RefreshAgentDetect();
        if (!string.IsNullOrEmpty(tab))
            PrefsTab = tab;
        Dialog = "prefs";
        Notify();
    }

    public void SetPrefsTab(string tab)
    {
        PrefsTab = tab;
        Notify();
    }

    public void SetWorkbench(string mode)
    {
        Workbench = mode == "req" ? "req" : "dev";
        ConsoleSettingsStore.SetWorkbench(Workbench);
        Notify();
        if (IsReqWorkbench)
            _ = RefreshIntakeAsync();
    }

    public void OpenWorkHours(WorkHoursView? view = null)
    {
        WorkHoursView = view ?? WorkHoursView.Week;
        WorkHoursAnchor = DateOnly.FromDateTime(DateTime.Now);
        WorkHoursProjectFilter = null;
        WorkHoursPane = "dash";
        Dialog = "hours";
        Notify();
        _ = RefreshWorkTimesheetAsync();
    }

    public void SetWorkHoursView(WorkHoursView view)
    {
        WorkHoursView = view;
        Notify();
        _ = RefreshWorkTimesheetAsync();
    }

    public void SetWorkHoursPane(string pane)
    {
        WorkHoursPane = pane is "sheet" or "chart" ? pane : "dash";
        Notify();
        if (WorkHoursPane == "sheet")
            _ = RefreshWorkTimesheetAsync();
        if (WorkHoursPane == "chart")
            _ = RefreshCompanyAssignmentsAsync();
    }

    public void SetWorkHoursProjectFilter(string? key)
    {
        WorkHoursProjectFilter = string.IsNullOrEmpty(key) ? null : key;
        Notify();
    }

    public void ShiftWorkHours(int steps)
    {
        if (steps == 0)
            return;
        if (steps > 0 && !WorkHoursReport.CanGoNext(DateOnly.FromDateTime(DateTime.Now)))
            return;
        WorkHoursAnchor = WorkHoursAggregator.Shift(WorkHoursView, WorkHoursAnchor, steps);
        Notify();
        _ = RefreshWorkTimesheetAsync();
    }

    public void OpenWorkHoursBucket(DateOnly start)
    {
        if (WorkHoursView is WorkHoursView.Year or WorkHoursView.Quarter)
            WorkHoursView = WorkHoursView.Month;
        WorkHoursAnchor = start;
        Notify();
        _ = RefreshWorkTimesheetAsync();
    }

    public void CloseWorkSession() => _workHours.End();

    public async Task RefreshWorkTimesheetAsync()
    {
        var gen = ++_timesheetGen;
        var now = DateTimeOffset.Now;
        var sheet = WorkHoursAggregator.BuildTimesheet(
            _workHours.VisibleSessions,
            WorkHoursView,
            WorkHoursAnchor,
            now,
            _workHours.PersonKey,
            _workHours.PersonLabel);
        WorkTimesheet = sheet;
        WorkHoursTimesheetHint = "";
        WorkHoursTimesheetBusy = true;
        Notify();
        try
        {
            var items = new List<TimesheetItem>();
            var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in sheet.Projects)
            {
                if (gen != _timesheetGen)
                    return;
                if (!project.HasGithub)
                {
                    notes[project.Key] = "未接 GitHub";
                    continue;
                }
                try
                {
                    var found = await WorkHoursContributions.LoadAsync(
                        project.Key,
                        project.Name,
                        project.GithubSlug,
                        sheet.RangeStart,
                        sheet.RangeEnd,
                        project.ProjectRoot,
                        _cts.Token,
                        ActiveGitHost).ConfigureAwait(false);
                    items.AddRange(found);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    notes[project.Key] = FirstLine(ex.Message);
                }
            }
            if (gen != _timesheetGen)
                return;
            WorkTimesheet = sheet.WithContributions(items, notes);
        }
        finally
        {
            if (gen == _timesheetGen)
            {
                WorkHoursTimesheetBusy = false;
                Notify();
            }
        }
    }

    public async Task ExportWorkTimesheetAsync()
    {
        if (WorkHoursExporting)
            return;
        if (WorkTimesheet is null || WorkHoursTimesheetBusy)
            await RefreshWorkTimesheetAsync().ConfigureAwait(false);
        var sheet = WorkTimesheet;
        if (sheet is null)
            return;
        if (WorkHoursPerson.IsLocal(_workHours.PersonKey))
        {
            if (!_native.Confirm(
                "尚未用 GitHub 歸戶",
                "目前是本機使用者身分。登入 GitHub 後 person_key 才穩定，PM 才好把多人檔案對到同一人。\n\n仍要匯出？"))
                return;
        }
        var folder = await _native.PickFolderAsync("選擇工時單匯出資料夾").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(folder))
            return;
        WorkHoursExporting = true;
        Notify();
        try
        {
            var files = WorkHoursExport.Write(folder, sheet);
            JobText = "已匯出工時單";
            _native.Info("已匯出工時單", "已寫入：\n" + string.Join("\n", files.Select(Path.GetFileName)));
        }
        catch (Exception ex)
        {
            _native.Error("匯出工時單失敗", ex.Message);
        }
        finally
        {
            WorkHoursExporting = false;
            Notify();
        }
    }

    public void OnAgentProviderChanged(string id)
    {
        AgentProvider = id;
        RefreshAgentDetect();
        Notify();
    }

    public void RefreshAgentDetectUi()
    {
        RefreshAgentDetect();
        Notify();
    }

    public void SavePreferences()
    {
        ConsoleSettingsStore.SetAgentProvider(AgentProvider);
        ConsoleSettingsStore.SetAgentCliPath(AgentCliPath);
        ConsoleSettingsStore.SetCustomAgentCommand(CustomAgentCommand);
        ConsoleSettingsStore.SetCustomAgentArgs(CustomAgentArgs);
        ConsoleSettingsStore.SetAskBaseUrl(AskBaseUrl);
        ConsoleSettingsStore.SetAskApiKey(AskApiKey);
        ConsoleSettingsStore.SetAskModel(AskModel);
        ConsoleSettingsStore.SetAskSources(AskSources);
        ConsoleSettingsStore.SetTheme(Theme);
        ConsoleSettingsStore.SetRestoreLastProject(RestoreLastProject);
        ConsoleSettingsStore.SetTestBeforePush(TestBeforePush);
        ConsoleSettingsStore.SetOpenIdeOnLoad(OpenWithCursor);
        ConsoleSettingsStore.SetGitHost(GitHostName);
        ConsoleSettingsStore.SetGitKind(GitKind);
        ConsoleSettingsStore.SetCompanyBaseUrl(CompanyBaseUrl);
        ConsoleSettingsStore.SetMcpReadOnly(McpReadOnly);
        ConsoleSettingsStore.SetMcpAllow(McpAllow);
        ConsoleSettingsStore.SetMcpDeny(McpDeny);
        ConsoleSettingsStore.SetMcpConfirm(McpConfirm);
        RefreshAgentDetect();
        CloseDialog();
        JobText = $"已儲存設定（Agent：{AgentDisplayName}）";
        RefreshAudit();
        Notify();
    }

    public string McpSnippet => McpLaunch.CursorSnippet(Catalog?.Root);

    public string McpToolMode =>
        McpReadOnly ? "readonly" :
        McpPolicy.SplitNames(McpAllow).Count > 0 ? "custom" : "all";

    public IReadOnlyList<StackToolSpec> McpTools => StackToolRouter.Tools;

    public IReadOnlyList<StackToolSpec> VisibleMcpTools
    {
        get
        {
            if (McpToolMode != "custom")
                return StackToolRouter.Tools;
            var allow = new HashSet<string>(McpPolicy.SplitNames(McpAllow), StringComparer.OrdinalIgnoreCase);
            return StackToolRouter.Tools.Where(t => allow.Contains(t.Name)).ToList();
        }
    }

    public IEnumerable<StackToolSpec> McpToolsToAdd
    {
        get
        {
            var have = new HashSet<string>(McpPolicy.SplitNames(McpAllow), StringComparer.OrdinalIgnoreCase);
            return StackToolRouter.Tools.Where(t => !have.Contains(t.Name));
        }
    }

    public int LinkedMcpCount => ProjectMcpServers.Count(s => s.Linked);

    public bool ConsoleMcpLinked => ProjectMcpServers.Any(s => s.Ours && s.Linked);

    public bool SelectedMcpIsCustom =>
        string.Equals(AddMcpId, McpLaunch.CustomPickerId, StringComparison.OrdinalIgnoreCase);

    public bool CustomMcpReady =>
        McpLaunch.IsValidServerId(CustomMcpId)
        && (CustomMcpKind == "url"
            ? !string.IsNullOrWhiteSpace(CustomMcpUrl)
            : !string.IsNullOrWhiteSpace(CustomMcpCommand));

    public bool SelectedMcpAlreadyLinked =>
        !SelectedMcpIsCustom
        && ProjectMcpServers.Any(s =>
            s.Linked && string.Equals(s.Id, AddMcpId, StringComparison.OrdinalIgnoreCase));

    public bool CanAddSelectedMcp =>
        HasProject && (SelectedMcpIsCustom ? CustomMcpReady : !SelectedMcpAlreadyLinked);

    public string AddMcpButtonLabel =>
        !HasProject ? "加入到專案" :
        SelectedMcpIsCustom ? "加入自訂" :
        SelectedMcpAlreadyLinked ? "已加入" :
        "加入到專案";

    public IEnumerable<ProjectMcpServer> AddableMcpServers
    {
        get
        {
            var unlinked = ProjectMcpServers.Where(s => !s.Linked);
            return unlinked.Append(new ProjectMcpServer(
                McpLaunch.CustomPickerId,
                "自訂…",
                "命令或遠端 URL",
                Linked: false,
                Ours: false,
                Suggested: false));
        }
    }

    public void SetAddMcpId(string? id)
    {
        AddMcpId = string.IsNullOrWhiteSpace(id) ? McpLaunch.ServerId : id.Trim();
        Notify();
    }

    public async Task CopyMcpConfigAsync()
    {
        try
        {
            if (Js is not null)
                await Js.InvokeVoidAsync("aiConsole.copyText", McpSnippet).ConfigureAwait(false);
            JobText = "已複製 Cursor／Claude Code 的 MCP 設定";
        }
        catch (Exception ex)
        {
            _native.Warn("複製失敗", ex.Message);
        }
        Notify();
    }

    public void SetCustomMcpKind(string kind)
    {
        CustomMcpKind = kind == "url" ? "url" : "command";
        Notify();
    }

    public void SetCustomMcpId(string value)
    {
        CustomMcpId = value ?? "";
        Notify();
    }

    public void SetCustomMcpCommand(string value)
    {
        CustomMcpCommand = value ?? "";
        Notify();
    }

    public void SetCustomMcpArgs(string value)
    {
        CustomMcpArgs = value ?? "";
        Notify();
    }

    public void SetCustomMcpUrl(string value)
    {
        CustomMcpUrl = value ?? "";
        Notify();
    }

    public void WriteMcpServer(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || string.Equals(id, McpLaunch.CustomPickerId, StringComparison.OrdinalIgnoreCase))
            return;
        SetAddMcpId(id);
        WriteMcpConfigToProject();
    }

    public void WriteMcpConfigToProject()
    {
        if (!RequireCatalog())
            return;
        try
        {
            string path;
            string hint;
            if (SelectedMcpIsCustom)
            {
                if (!CustomMcpReady)
                {
                    _native.Info("資料不完整", "請填識別名稱，以及命令或遠端 URL。");
                    return;
                }

                path = McpLaunch.WriteCustomServer(
                    Catalog!.Root,
                    CustomMcpId.Trim(),
                    CustomMcpKind == "url" ? null : CustomMcpCommand,
                    CustomMcpKind == "url" ? null : CustomMcpArgs,
                    CustomMcpKind == "url" ? CustomMcpUrl : null);
                hint = "已加入自訂 MCP " + CustomMcpId.Trim();
                CustomMcpId = "";
                CustomMcpArgs = "";
                CustomMcpUrl = "";
            }
            else
            {
                path = McpLaunch.WriteCatalogServer(Catalog!.Root, AddMcpId);
                hint = McpLaunch.FindCatalog(AddMcpId)?.AfterAddHint
                    ?? ("已加入 " + AddMcpId);
            }

            RefreshMcpPrefsUi();
            SelectNextAddable();
            JobText = hint + " → " + path + "（重新載入 Cursor 後生效）";
        }
        catch (Exception ex)
        {
            _native.Error("寫入 MCP 設定失敗", ex.Message);
        }
        Notify();
    }

    public void RemoveMcpFromProject(string id)
    {
        if (!RequireCatalog() || string.IsNullOrWhiteSpace(id))
            return;
        if (!_native.Confirm(
                "移除 MCP",
                $"從專案 .cursor/mcp.json 移除「{id}」？\n不會解除安裝套件，只拿掉這份專案設定。"))
            return;
        try
        {
            var path = McpLaunch.RemoveServer(Catalog!.Root, id);
            RefreshMcpPrefsUi();
            SetAddMcpId(id);
            JobText = "已從 " + path + " 移除 " + id;
        }
        catch (Exception ex)
        {
            _native.Error("移除 MCP 失敗", ex.Message);
        }
        Notify();
    }

    public void OpenMcpConfig()
    {
        if (!RequireCatalog())
            return;
        var path = McpLaunch.CursorConfigPath(Catalog!.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
            File.WriteAllText(path, "{\n  \"mcpServers\": {}\n}\n");
        CliUtil.OpenPath(path);
    }

    void SelectNextAddable()
    {
        var next = AddableMcpServers.FirstOrDefault(s =>
            !string.Equals(s.Id, McpLaunch.CustomPickerId, StringComparison.OrdinalIgnoreCase));
        AddMcpId = next?.Id ?? McpLaunch.CustomPickerId;
    }

    public void WriteMcpPolicyToProject()
    {
        if (!RequireCatalog())
            return;
        try
        {
            var path = McpPolicy.WriteTemplate(Catalog!.Root);
            RefreshMcpPrefsUi();
            JobText = "已寫入 " + path + "（專案政策會覆蓋使用者設定）";
        }
        catch (Exception ex)
        {
            _native.Error("寫入 MCP 政策失敗", ex.Message);
        }
        Notify();
    }

    public void SetMcpToolMode(string mode)
    {
        switch (mode)
        {
            case "readonly":
                McpReadOnly = true;
                break;
            case "custom":
                McpReadOnly = false;
                if (McpPolicy.SplitNames(McpAllow).Count == 0)
                {
                    var deny = new HashSet<string>(McpPolicy.SplitNames(McpDeny), StringComparer.OrdinalIgnoreCase);
                    McpAllow = JoinMcpNames(StackToolRouter.Tools.Select(t => t.Name).Where(n => !deny.Contains(n)));
                }
                if (string.IsNullOrEmpty(AddToolName))
                    AddToolName = McpToolsToAdd.FirstOrDefault()?.Name ?? "";
                break;
            default:
                McpReadOnly = false;
                McpAllow = "";
                break;
        }
        Notify();
    }

    public string GetMcpToolAccess(string name)
    {
        var key = (name ?? "").Trim().ToLowerInvariant();
        if (HasMcpName(McpDeny, key))
            return "deny";
        if (McpReadOnly && !McpPolicy.ReadOnlyTools.Contains(key, StringComparer.OrdinalIgnoreCase))
            return "deny";
        if (McpToolMode == "custom" && McpPolicy.SplitNames(McpAllow).Count > 0 && !HasMcpName(McpAllow, key))
            return "deny";
        if (HasMcpName(McpConfirm, key))
            return "confirm";
        return "allow";
    }

    public bool McpToolAccessLocked(string name)
    {
        var key = (name ?? "").Trim().ToLowerInvariant();
        return McpReadOnly && !McpPolicy.ReadOnlyTools.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    public void SetMcpToolAccess(string name, string access)
    {
        var key = (name ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key) || McpToolAccessLocked(key))
            return;

        var deny = McpPolicy.SplitNames(McpDeny).Where(n => n != key).ToList();
        var confirm = McpPolicy.SplitNames(McpConfirm).Where(n => n != key).ToList();
        var allow = McpPolicy.SplitNames(McpAllow).ToList();

        switch (access)
        {
            case "deny":
                deny.Add(key);
                allow.RemoveAll(n => n == key);
                break;
            case "confirm":
                confirm.Add(key);
                if (McpToolMode == "custom" && !allow.Contains(key, StringComparer.OrdinalIgnoreCase))
                    allow.Add(key);
                break;
            default:
                if (McpToolMode == "custom" && !allow.Contains(key, StringComparer.OrdinalIgnoreCase))
                    allow.Add(key);
                break;
        }

        McpDeny = JoinMcpNames(deny);
        McpConfirm = JoinMcpNames(confirm);
        if (McpToolMode == "custom")
            McpAllow = JoinMcpNames(allow);
        Notify();
    }

    public void AddMcpTool(string? name = null)
    {
        var key = (name ?? AddToolName ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key) || !StackToolRouter.Tools.Any(t => t.Name == key))
            return;
        if (McpToolMode != "custom")
            SetMcpToolMode("custom");
        var allow = McpPolicy.SplitNames(McpAllow).ToList();
        if (!allow.Contains(key, StringComparer.OrdinalIgnoreCase))
            allow.Add(key);
        var deny = McpPolicy.SplitNames(McpDeny).Where(n => n != key).ToList();
        McpAllow = JoinMcpNames(allow);
        McpDeny = JoinMcpNames(deny);
        AddToolName = McpToolsToAdd.FirstOrDefault()?.Name ?? "";
        Notify();
    }

    public void RemoveMcpTool(string name)
    {
        var key = (name ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key))
            return;
        if (McpToolMode != "custom")
            SetMcpToolMode("custom");
        if (McpPolicy.SplitNames(McpAllow).Count <= 1)
            return;
        McpAllow = JoinMcpNames(McpPolicy.SplitNames(McpAllow).Where(n => n != key));
        McpDeny = JoinMcpNames(McpPolicy.SplitNames(McpDeny).Where(n => n != key));
        McpConfirm = JoinMcpNames(McpPolicy.SplitNames(McpConfirm).Where(n => n != key));
        if (string.IsNullOrEmpty(AddToolName))
            AddToolName = key;
        Notify();
    }

    void RefreshMcpPrefsUi()
    {
        ProjectMcpServers = McpLaunch.ListReferenced(Catalog?.Root);
        ProjectHasMcpPolicy = Catalog is not null && File.Exists(McpPolicy.FilePath(Catalog.Root));
        if (string.IsNullOrEmpty(AddToolName))
            AddToolName = McpToolsToAdd.FirstOrDefault()?.Name ?? "";
    }

    static bool HasMcpName(string csv, string key) =>
        McpPolicy.SplitNames(csv).Contains(key, StringComparer.OrdinalIgnoreCase);

    static string JoinMcpNames(IEnumerable<string> names) =>
        string.Join(", ", names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase));


    public void SetRestoreLastProject(bool value)
    {
        RestoreLastProject = value;
        ConsoleSettingsStore.SetRestoreLastProject(value);
        Notify();
    }

    public void SetTestBeforePush(bool value)
    {
        TestBeforePush = value;
        ConsoleSettingsStore.SetTestBeforePush(value);
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
        return CloseProjectCoreAsync();
    }

    private async Task CloseProjectCoreAsync()
    {
        if (Catalog is null)
            return;
        if (!await EnsureClearToLeaveAsync("關閉專案").ConfigureAwait(false))
            return;

        var name = Catalog.Name;
        var running = Catalog.Services.Where(s => Health.GetValueOrDefault(s.Id)).Select(s => s.Label).ToList();
        var body = running.Count > 0
            ? $"確定關閉「{name}」？\n目前有 {running.Count} 個服務在執行，關閉時會一併停止，並回到尚未選擇專案的狀態。"
            : $"確定關閉「{name}」？\n會回到尚未選擇專案的狀態。";
        if (!_native.Confirm("關閉專案", body))
            return;

        var closeIde = ConfirmCloseLocalAgent();
        if (Catalog is not null && Runtime is not null)
        {
            try
            {
                ProcessSupervisor.StopAll(Catalog, Runtime);
            }
            catch (Exception ex)
            {
                if (!_native.Confirm("停止服務失敗", $"停止服務時發生問題：\n{ex.Message}\n\n仍要關閉專案嗎？"))
                    return;
            }
        }

        if (closeIde)
        {
            var err = CurrentAgent.CloseIde(Catalog?.Root);
            if (err is not null)
                _native.Warn($"關閉 {AgentDisplayName}", err);
        }

        var closedRoot = Catalog?.Root;
        ResetToStartup();
        try
        {
            ConsoleSettingsStore.ClearLastProjectIf(closedRoot);
        }
        catch
        {
            // 歷史仍保留，還原標記失敗不阻擋關閉
        }
        JobText = closeIde
            ? (CurrentAgent.Kind == AgentBackendKind.Ide
                ? $"已關閉專案，並關閉這個專案的 {AgentDisplayName} 視窗"
                : $"已關閉專案，並關閉 {AgentDisplayName}")
            : "已關閉專案";
        Notify();
    }

    public async Task LoadProjectAsync(string root, bool openCursor = false)
    {
        try
        {
            root = Path.GetFullPath(root);
            if (!await TryStopCurrentProjectForSwitchAsync(root).ConfigureAwait(false))
                return;

            var catalog = ServiceCatalogBuilder.Build(root);
            _iconUrlCache.Clear();
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
            foreach (var svc in catalog.Services)
            {
                if (ServiceCatalogBuilder.IsCurrentConsole(catalog, svc))
                    Health[svc.Id] = true;
            }
            StartErrors.Clear();
            ServiceActivities.Clear();
            _collapsedServiceGroups.Clear();
            _collapsedProjectGroups.Clear();
            LogFilter = "";
            GitStatusText = "";
            ClearIssueLists();
            ReloadLog();
            LoadAudit(reloadPolicy: true);
            try
            {
                McpLaunch.TryRepairOurs(catalog.Root);
            }
            catch
            {
                // 舊 mcp.json 修補失敗不擋載入
            }
            RefreshMcpPrefsUi();
            JobText = rememberErr is null ? "已載入專案" : $"已載入專案（歷史未寫入：{rememberErr}）";
            _workHours.SwitchProject(catalog.Root, catalog.Name);
            RefreshDocs();
            Notify();
            _ = RefreshBuildStatesAsync();
            await AfterProjectLoadedAsync(openCursor, rememberErr).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _native.Error("開啟專案失敗", ex.Message);
        }
    }

    public async Task RefreshWorkspaceAsync()
    {
        if (Catalog is null)
        {
            _native.Info("尚未選擇專案", "請先選擇專案目錄。");
            return;
        }
        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return;
        }

        var previous = Catalog;
        var root = previous.Root;
        JobBusy = true;
        JobText = "正在重新掃描專案目錄…";
        Notify();
        try
        {
            var next = await Task.Run(() => ServiceCatalogBuilder.Build(root)).ConfigureAwait(false);
            var diff = CatalogRefresh.Diff(previous, next);
            ApplyRefreshedCatalog(next);
            JobBusy = false;
            JobText = diff.Format();
            UpdateReady();
            Notify();
            await RefreshBuildStatesAsync(clearActivity: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            JobBusy = false;
            JobText = "重新掃描失敗";
            Notify();
            _native.Error("重新掃描失敗", ex.Message);
        }
    }

    private void ApplyRefreshedCatalog(ProjectCatalog next)
    {
        var keep = next.Services.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Health.Keys.Where(k => !keep.Contains(k)).ToList())
            Health.Remove(id);
        foreach (var id in StartErrors.Keys.Where(k => !keep.Contains(k)).ToList())
            StartErrors.Remove(id);
        foreach (var svc in next.Services)
        {
            if (ServiceCatalogBuilder.IsCurrentConsole(next, svc))
                Health[svc.Id] = true;
        }
        if (SelectedServiceId is null || !keep.Contains(SelectedServiceId))
            SelectedServiceId = next.Services.FirstOrDefault()?.Id;
        _iconUrlCache.Clear();
        Catalog = next;
        ReloadLog();
    }

    private async Task<bool> TryStopCurrentProjectForSwitchAsync(string nextRoot)
    {
        if (Catalog is null || Runtime is null)
            return true;
        if (string.Equals(Catalog.Root, nextRoot, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!await EnsureClearToLeaveAsync("切換專案").ConfigureAwait(false))
            return false;

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
        StopDocsServe();
        return true;
    }

    private async Task AfterProjectLoadedAsync(bool openCursor, string? rememberErr)
    {
        try
        {
            await StampWorkHoursProjectAsync().ConfigureAwait(false);
            await RefreshGitStatusAsync().ConfigureAwait(false);
            await RefreshGithubAuthAsync().ConfigureAwait(false);
            var catalog = Catalog;
            GithubManaged = catalog is not null && await GitHubService.IsGithubManagedAsync(catalog).ConfigureAwait(false);
            if (GithubManaged && !GithubLoggedIn)
            {
                _pendingOpenCursor = openCursor;
                GithubAuthHint = GitHubService.GhAvailable()
                    ? "此專案由 GitHub 管理。請先登入，才能讀取指派給你的 Issue。"
                    : "此專案由 GitHub 管理，但尚未安裝 GitHub CLI（gh）。請先安裝 https://cli.github.com/ 再登入。";
                Dialog = "gh-login";
                Notify();
                return;
            }
            await FinishProjectOpenAsync(openCursor, rememberErr).ConfigureAwait(false);
            if (GithubLoggedIn && GithubManaged)
            {
                await RefreshIssuesAsync().ConfigureAwait(false);
                await RefreshActionsAsync().ConfigureAwait(false);
                await RefreshPullRequestAsync().ConfigureAwait(false);
            }
            await RefreshIntakeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            JobText = "已載入專案";
            _native.Warn("GitHub", FirstLine(ex.Message));
            Notify();
        }
    }

    private async Task FinishProjectOpenAsync(bool openCursor, string? rememberErr)
    {
        await AutoSyncOnOpenAsync().ConfigureAwait(false);
        if (openCursor && Catalog is not null)
        {
            var backend = CurrentAgent;
            var err = backend.OpenWorkspace(Catalog.Root, AgentBackendRegistry.CliOverrideFor(backend));
            JobText = err is null
                ? (rememberErr is null
                    ? (string.IsNullOrEmpty(JobText) || JobText == "已載入專案" ? $"已載入專案，並在 {backend.DisplayName} 開啟" : JobText)
                    : JobText)
                : $"已載入專案（{backend.DisplayName} 未開啟：{err}）";
        }
        Notify();
    }

    private async Task AutoSyncOnOpenAsync()
    {
        if (Catalog is null)
            return;
        if (!await GitHubService.IsGitRepoAsync(Catalog.Root).ConfigureAwait(false))
            return;
        if (!await GitHubService.HasRemoteAsync(Catalog.Root).ConfigureAwait(false))
            return;

        JobBusy = true;
        JobText = "正在從遠端同步…";
        Notify();
        try
        {
            await RefreshGitStatusAsync().ConfigureAwait(false);
            var dirty = GitBrief?.DirtyCount ?? 0;
            if (dirty > 0)
            {
                _autoSyncSkippedDirty = true;
                JobText = AutoSyncSkippedDirtyText;
                _native.Warn(
                    "無法自動同步",
                    $"工作區有 {dirty} 筆未提交變更。請先提交或還原後再同步，避免本機與遠端不一致。");
                return;
            }
            _autoSyncSkippedDirty = false;
            await GitHubService.SyncFromRemoteAsync(Catalog).ConfigureAwait(false);
            JobText = "已從遠端同步";
        }
        catch (Exception ex)
        {
            JobText = "自動同步失敗";
            _native.Warn("自動同步失敗", FirstLine(ex.Message));
        }
        finally
        {
            JobBusy = false;
            await RefreshGitStatusAsync().ConfigureAwait(false);
            Notify();
        }
    }

    private async Task<bool> EnsureClearToLeaveAsync(string action)
    {
        if (Catalog is null)
            return true;
        if (!await GitHubService.IsGitRepoAsync(Catalog.Root).ConfigureAwait(false))
            return true;
        await RefreshGitStatusAsync().ConfigureAwait(false);
        var reason = GitBrief?.LeaveBlockReason();
        if (reason is null)
            return true;

        _leaveGateTcs?.TrySetResult(false);
        _leaveGateTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        LeaveGateAction = action;
        LeaveGateReason = reason;
        Dialog = "leave-gate";
        Notify();
        return await _leaveGateTcs.Task.ConfigureAwait(false);
    }

    public async Task RefreshGithubAuthAsync()
    {
        try
        {
            GithubAccount = await GitHubAuth.CurrentAsync(Catalog?.Root, ActiveGitHost, _cts.Token).ConfigureAwait(false);
        }
        catch
        {
            GithubAccount = GitHubService.GhAvailable() ? new GithubAccount("", true) : GithubAccount.None;
        }
        AttachWorkHoursPerson();
        Notify();
    }

    void AttachWorkHoursPerson()
    {
        if (!GithubLoggedIn)
            return;
        _workHours.Identify(WorkHoursPerson.GithubKey(GithubAccount.Login, GithubAccount.Host), GithubAccount.Display());
    }

    public async Task LoginGithubAsync()
    {
        if (GithubAuthBusy)
            return;
        if (!GitHubService.GhAvailable())
        {
            GithubAuthHint = "尚未安裝 GitHub CLI（gh）。請先安裝 https://cli.github.com/ 再登入。";
            _native.Error("需要 GitHub CLI", GithubAuthHint);
            Notify();
            return;
        }

        _githubLoginCts?.Cancel();
        _githubLoginCts = new CancellationTokenSource();
        ClearGithubLoginPrompt();
        Dialog = "gh-login";
        GithubAuthBusy = true;
        GithubAuthHint = "正在向 GitHub 取得授權碼…";
        JobText = "等待 GitHub 登入…";
        Notify();
        try
        {
            ConsoleSettingsStore.SetGitHost(ActiveGitHost);
            ConsoleSettingsStore.SetGitKind(GitKind);
            GitHostName = ActiveGitHost;
            var progress = new Progress<GithubLoginPrompt>(ApplyGithubLoginPrompt);
            var (ok, message) = await GitHubAuth.LoginWebAsync(
                Catalog?.Root, ActiveGitHost, progress, _githubLoginCts.Token).ConfigureAwait(false);
            await RefreshGithubAuthAsync().ConfigureAwait(false);
            if (ok && GithubLoggedIn)
            {
                ClearGithubLoginPrompt();
                GithubAuthHint = "";
                JobText = $"已登入 GitHub（{GithubAccount.Display()}）";
                if (Dialog == "gh-login")
                    Dialog = null;
                var openCursor = _pendingOpenCursor;
                _pendingOpenCursor = false;
                if (Catalog is not null)
                    await FinishProjectOpenAsync(openCursor, rememberErr: null).ConfigureAwait(false);
                if (GithubManaged && Catalog is not null)
                {
                    await RefreshIssuesAsync().ConfigureAwait(false);
                    await RefreshActionsAsync().ConfigureAwait(false);
                    await RefreshPullRequestAsync().ConfigureAwait(false);
                }
                await RefreshIntakeAsync().ConfigureAwait(false);
                Notify();
                return;
            }
            ClearGithubLoginPrompt();
            GithubAuthHint = GithubLoggedIn ? "" : (string.IsNullOrEmpty(message) ? "登入未完成。" : FirstLine(message));
            JobText = "GitHub 尚未登入";
            Dialog = "gh-login";
            _native.Warn("GitHub 登入", GithubAuthHint);
        }
        catch (OperationCanceledException)
        {
            ClearGithubLoginPrompt();
            if (Catalog is not null)
            {
                GithubAuthHint = "已取消登入。";
                JobText = "已取消 GitHub 登入";
            }
        }
        finally
        {
            GithubAuthBusy = false;
            Notify();
        }
    }

    void ClearGithubLoginPrompt()
    {
        GithubLoginCode = "";
        GithubLoginUrl = "";
        GithubLoginBrowserOpened = false;
        GithubLoginCodeCopied = false;
    }

    void ApplyGithubLoginPrompt(GithubLoginPrompt prompt)
    {
        var codeArrived = !string.IsNullOrEmpty(prompt.DeviceCode) && prompt.DeviceCode != GithubLoginCode;
        GithubLoginCode = prompt.DeviceCode;
        GithubLoginUrl = prompt.BrowserUrl;
        GithubLoginBrowserOpened = prompt.BrowserOpened;
        if (!string.IsNullOrEmpty(prompt.DeviceCode) && prompt.BrowserOpened)
            GithubAuthHint = "請把一次性代碼貼到瀏覽器的 GitHub 授權頁。";
        else if (!string.IsNullOrEmpty(prompt.DeviceCode) && !string.IsNullOrEmpty(prompt.BrowserUrl))
            GithubAuthHint = "瀏覽器沒有自動開啟。請按「在瀏覽器開啟」，並把代碼貼到授權頁。";
        else if (!string.IsNullOrEmpty(prompt.DeviceCode))
            GithubAuthHint = "已取得一次性代碼，正在開啟瀏覽器…";
        Notify();
        if (codeArrived)
            _ = CopyGithubLoginCodeAsync();
    }

    public async Task CopyGithubLoginCodeAsync()
    {
        if (string.IsNullOrEmpty(GithubLoginCode) || Js is null)
            return;
        try
        {
            await Js.InvokeVoidAsync("aiConsole.copyText", GithubLoginCode).ConfigureAwait(false);
            GithubLoginCodeCopied = true;
            JobText = "已複製一次性代碼。";
        }
        catch
        {
            GithubLoginCodeCopied = false;
            JobText = "無法複製到剪貼簿，請手動選取代碼。";
        }
        Notify();
    }

    public void OpenGithubLoginUrl()
    {
        if (string.IsNullOrEmpty(GithubLoginUrl))
            return;
        try
        {
            CliUtil.OpenUrl(GithubLoginUrl);
            GithubLoginBrowserOpened = true;
            GithubAuthHint = "請把一次性代碼貼到瀏覽器的 GitHub 授權頁。";
        }
        catch (Exception ex)
        {
            GithubLoginBrowserOpened = false;
            GithubAuthHint = "無法開啟瀏覽器，請按網址手動打開。";
            _native.Warn("開啟瀏覽器", ex.Message);
        }
        Notify();
    }

    public async Task LogoutGithubAsync()
    {
        if (GithubAuthBusy || !GithubLoggedIn)
            return;
        if (!_native.Confirm("登出 GitHub", $"要登出 {GithubAccount.Display()} 嗎？\n登出後，GitHub 管理的專案需要重新登入。"))
            return;

        GithubAuthBusy = true;
        JobText = "登出 GitHub…";
        Notify();
        try
        {
            var (ok, message) = await GitHubAuth.LogoutAsync(Catalog?.Root, ActiveGitHost, _cts.Token).ConfigureAwait(false);
            await RefreshGithubAuthAsync().ConfigureAwait(false);
            ClearIssueLists();
            if (!ok && GithubLoggedIn)
            {
                _native.Warn("登出 GitHub", string.IsNullOrEmpty(message) ? "登出失敗。" : message);
                JobText = "GitHub 登出失敗";
                return;
            }
            JobText = "已登出 GitHub";
            if (GithubManaged && Catalog is not null)
            {
                GithubAuthHint = "此專案由 GitHub 管理。請重新登入，或關閉專案。";
                Dialog = "gh-login";
            }
        }
        finally
        {
            GithubAuthBusy = false;
            Notify();
        }
    }

    public void CancelGithubLogin()
    {
        _githubLoginCts?.Cancel();
        GithubAuthBusy = false;
        _pendingOpenCursor = false;
        ClearGithubLoginPrompt();
        if (GithubManaged && Catalog is not null && !GithubLoggedIn)
            DropProjectAfterLoginCancel();
        else
        {
            Dialog = null;
            Notify();
        }
    }

    private void DropProjectAfterLoginCancel()
    {
        var closedRoot = Catalog?.Root;
        ResetToStartup();
        try
        {
            ConsoleSettingsStore.ClearLastProjectIf(closedRoot);
        }
        catch
        {
            // 未寫入還原標記不阻擋關閉
        }
        JobText = "已取消登入，專案未開啟";
        Notify();
    }

    public async Task RefreshIssuesAsync()
    {
        if (Catalog is null || !GithubLoggedIn || !GithubManaged)
        {
            ClearIssueLists();
            if (Catalog is not null && !GithubManaged)
                IssuesHint = "此專案尚未接上 GitHub，沒有 Issue 任務。";
            else if (Catalog is not null && !GithubLoggedIn)
                IssuesHint = "登入 GitHub 後即可看到指派給你的任務。";
            Notify();
            return;
        }
        if (IssuesBusy)
            return;
        IssuesBusy = true;
        IssuesHint = "載入任務中…";
        Notify();
        try
        {
            var all = await GitHubIssues.ListOpenAsync(Catalog, ct: _cts.Token).ConfigureAwait(false);
            var (mine, open) = GitHubIssues.Split(all, GithubAccount.Login);
            AssignedIssues = mine;
            UnassignedIssues = open;
            try
            {
                var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
                var traces = await GitHubLifecycle.LoadTracesAsync(
                    Catalog.Root, cfg, mine.Select(i => i.Number), _cts.Token).ConfigureAwait(false);
                var map = traces.ToDictionary(t => t.Number);
                AssignedIssues = mine.Select(i => map.TryGetValue(i.Number, out var tr)
                    ? i with { CiTone = tr.CiTone, CiHint = tr.CiHint, PrUrl = tr.PrUrl, PrState = tr.PrState }
                    : i).ToList();
            }
            catch
            {
                // 任務清單已就緒；CI／PR 燈號可下次再補
            }
            if (!_unassignedCollapseUserSet)
                UnassignedCollapsed = mine.Count > 0;
            IssuesHint = mine.Count == 0 && open.Count == 0
                ? "目前沒有未關閉的 Issue。"
                : "";
        }
        catch (Exception ex)
        {
            IssuesHint = FirstLine(ex.Message);
        }
        finally
        {
            IssuesBusy = false;
            Notify();
        }
    }

    public async Task AcceptIssueAsync(int number)
    {
        if (!RequireCatalog() || !GithubLoggedIn)
            return;
        var issue = UnassignedIssues.FirstOrDefault(i => i.Number == number)
            ?? AssignedIssues.FirstOrDefault(i => i.Number == number);
        var title = issue?.Title ?? $"#{number}";
        if (!_native.Confirm("接受任務", $"要把 {issue?.NumberText ?? "#" + number}「{title}」指派給 {GithubAccount.Display()} 嗎？"))
            return;
        await RunJobAsync("接受任務…", async () =>
        {
            await GitHubIssues.AcceptAsync(Catalog!, number).ConfigureAwait(false);
            return $"已接受 {issue?.NumberText ?? "#" + number}";
        }).ConfigureAwait(false);
        await RefreshIssuesAsync().ConfigureAwait(false);
        OpenGithubHub();
    }

    public void OpenIssueUrl(GithubIssue issue)
    {
        if (!string.IsNullOrEmpty(issue.Url))
            CliUtil.OpenUrl(issue.Url);
    }

    public void ShowTasks()
    {
        OpenGithubHub();
    }

    public void OpenGithubHub()
    {
        AskPanelOpen = false;
        AuditPanelOpen = false;
        GithubHubOpen = true;
        Notify();
        if (GithubLoggedIn && GithubManaged)
            _ = RefreshIssuesAsync();
    }

    public void CloseGithubHub()
    {
        if (!GithubHubOpen)
            return;
        GithubHubOpen = false;
        Notify();
    }

    public void ToggleGithubHub()
    {
        if (GithubHubOpen)
            CloseGithubHub();
        else
            OpenGithubHub();
    }

    public async Task RunGithubNextAsync()
    {
        var next = GithubNext;
        if (next.OpensHub)
        {
            OpenGithubHub();
            return;
        }
        var action = GithubActions.FirstOrDefault(a => a.Handler == next.Handler);
        if (action is null)
        {
            OpenGithubHub();
            return;
        }
        await OnActionAsync(action).ConfigureAwait(false);
    }

    public void ShowTasksFromHub()
    {
        SetWorkbench("dev");
        OpenGithubHub();
    }

    private void ClearIssueLists()
    {
        AssignedIssues = [];
        UnassignedIssues = [];
        IssuesHint = "";
        IssuesBusy = false;
        UnassignedCollapsed = true;
        _unassignedCollapseUserSet = false;
        GithubManaged = false;
        ClearIssueView();
    }

    private static string FirstLine(string text)
    {
        var t = (text ?? "").Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }

    public void SelectService(string id)
    {
        SelectedServiceId = id;
        LeftTab = "svc";
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

    public void ShowStaleProjects()
    {
        LeftTab = "prj";
        StaleOnly = StaleProjectCount > 0;
        Notify();
    }

    public void ShowDocs()
    {
        LeftTab = "docs";
        RefreshDocs(keepSelection: true);
        Notify();
    }

    public void SetWorkspaceTab(string tab)
    {
        LeftTab = tab is "prj" or "docs" ? tab : "svc";
        Notify();
    }

    public void SetRightTab(string tab)
    {
        switch (tab)
        {
            case "audit":
                OpenAuditPanel();
                return;
            case "ask":
                OpenAskPanel();
                return;
            case "build":
                LeftTab = "prj";
                Notify();
                return;
            case "docs":
                ShowDocs();
                return;
            default:
                LeftTab = "svc";
                Notify();
                return;
        }
    }

    public void OpenAskPanel()
    {
        GithubHubOpen = false;
        AuditPanelOpen = false;
        AskPanelOpen = true;
        Notify();
    }

    public void CloseAskPanel()
    {
        if (!AskPanelOpen)
            return;
        AskPanelOpen = false;
        Notify();
    }

    public void ToggleAskPanel()
    {
        if (AskPanelOpen)
            CloseAskPanel();
        else
            OpenAskPanel();
    }

    public void OpenAuditPanel()
    {
        AskPanelOpen = false;
        AuditPanelOpen = true;
        RefreshAudit();
    }

    public void CloseAuditPanel()
    {
        if (!AuditPanelOpen)
            return;
        AuditPanelOpen = false;
        Notify();
    }

    public void SetAuditFilter(string value)
    {
        AuditFilter = value ?? "";
        Notify();
    }

    public void RefreshAudit()
    {
        LoadAudit(reloadPolicy: true);
        Notify();
    }

    public void OpenAuditDir()
    {
        if (Runtime is null)
            return;
        Runtime.Ensure();
        var path = McpAuditLog.FilePath(Runtime);
        CliUtil.OpenPath(File.Exists(path) ? path : Runtime.Base);
    }

    public void SetAskDraft(string value)
    {
        AskDraft = value ?? "";
        Notify();
    }

    public void SetAskBaseUrl(string value)
    {
        AskBaseUrl = value ?? "";
        ClearAskProbe();
        Notify();
    }

    public void SetAskApiKey(string value)
    {
        AskApiKey = value ?? "";
        ClearAskProbe();
        Notify();
    }

    public void SetAskModel(string value)
    {
        AskModel = value ?? "";
        if (AskProbeModels.Count > 0)
            AskProbeModelFound = ProjectAskService.ModelInList(AskModel, AskProbeModels);
        Notify();
    }

    public void ApplyAskProvider(string? id)
    {
        var provider = ProjectAskProviders.Get(id);
        if (provider.Id != ProjectAskProviders.CustomId)
        {
            AskBaseUrl = provider.BaseUrl;
            if (!string.IsNullOrEmpty(provider.DefaultModel))
                AskModel = provider.DefaultModel;
            if (!provider.NeedsApiKey)
                AskApiKey = "";
        }
        ClearAskProbe();
        Notify();
    }

    public void ApplyAskSource(string? id)
    {
        var source = AskSources.FirstOrDefault(s => s.Id.Equals((id ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return;
        AskBaseUrl = source.BaseUrl;
        AskModel = source.Model;
        AskApiKey = source.ApiKey ?? "";
        ClearAskProbe();
        Notify();
    }

    public void RememberAskSource()
    {
        if (string.IsNullOrWhiteSpace(AskBaseUrl))
            return;
        AskSources = ProjectAskProviders.Upsert(AskSources, AskBaseUrl, AskModel, AskApiKey);
        Notify();
    }

    public void RemoveAskSource(string? id)
    {
        AskSources = ProjectAskProviders.Remove(AskSources, id);
        Notify();
    }

    public async Task ProbeAskAsync()
    {
        if (AskProbeBusy)
            return;
        if (string.IsNullOrWhiteSpace(AskBaseUrl))
        {
            AskProbeOk = false;
            AskProbeModelFound = false;
            AskProbeModels = [];
            AskProbeMessage = "請先填 Base URL。";
            Notify();
            return;
        }

        _askProbeCts?.Cancel();
        _askProbeCts?.Dispose();
        _askProbeCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _askProbeCts.Token;
        AskProbeBusy = true;
        AskProbeMessage = "測試中…";
        AskProbeOk = false;
        AskProbeModelFound = false;
        Notify();
        try
        {
            var result = await ProjectAskService.ProbeAsync(
                new ProjectAskOptions(AskBaseUrl.Trim(), AskModel.Trim(), AskApiKey),
                ct: ct).ConfigureAwait(false);
            AskProbeOk = result.Ok;
            AskProbeModelFound = result.ModelFound;
            AskProbeMessage = result.Message;
            AskProbeModels = result.Models;
            if (result.Ok && result.Models.Count > 0 && string.IsNullOrWhiteSpace(AskModel))
            {
                AskModel = result.Models[0];
                AskProbeModelFound = true;
            }
        }
        catch (OperationCanceledException)
        {
            AskProbeMessage = "";
        }
        catch (Exception ex)
        {
            AskProbeOk = false;
            AskProbeModelFound = false;
            AskProbeModels = [];
            AskProbeMessage = ex.Message;
        }
        finally
        {
            AskProbeBusy = false;
            Notify();
        }
    }

    void ClearAskProbe()
    {
        _askProbeCts?.Cancel();
        AskProbeBusy = false;
        AskProbeOk = false;
        AskProbeModelFound = false;
        AskProbeMessage = "";
        AskProbeModels = [];
    }

    public Task SendAskSuggestionAsync(string prompt) => SendAskAsync(prompt);

    public async Task SendAskAsync(string? text = null)
    {
        var question = (text ?? AskDraft ?? "").Trim();
        if (string.IsNullOrEmpty(question) || AskBusy)
            return;
        if (!HasProject)
        {
            _native.Info("專案問答", "請先選擇專案目錄。");
            return;
        }
        if (!AskConfigured)
        {
            _native.Info("專案問答", "請先在設定填寫 Base URL 與模型（預設可用本機 Ollama）。");
            return;
        }

        if (text is null)
            AskDraft = "";
        var history = AskMessages.ToList();
        AskMessages = history.Append(new ProjectAskChatItem("user", question)).ToList();
        AskBusy = true;
        AskStatus = "思考中…";
        Notify();

        _askCts?.Cancel();
        _askCts?.Dispose();
        _askCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = _askCts.Token;
        var options = new ProjectAskOptions(AskBaseUrl.Trim(), AskModel.Trim(), AskApiKey);
        try
        {
            var workspace = StackWorkspace.Open(Catalog!.Root);
            var reply = await ProjectAskService.AskAsync(
                workspace,
                question,
                history,
                options,
                onStatus: status =>
                {
                    AskStatus = status;
                    Notify();
                },
                onTool: name =>
                {
                    AskMessages = AskMessages.Append(new ProjectAskChatItem("tool", StackToolRouter.DisplayTitle(name), name)).ToList();
                    Notify();
                },
                ct: ct).ConfigureAwait(false);
            AskMessages = AskMessages.Append(new ProjectAskChatItem("assistant", reply)).ToList();
        }
        catch (OperationCanceledException)
        {
            AskMessages = AskMessages.Append(new ProjectAskChatItem("assistant", "已取消。")).ToList();
        }
        catch (Exception ex)
        {
            AskMessages = AskMessages.Append(new ProjectAskChatItem("error", ex.Message)).ToList();
        }
        finally
        {
            AskBusy = false;
            AskStatus = "";
            Notify();
        }
    }

    public void CancelAsk()
    {
        _askCts?.Cancel();
    }

    public async Task StartAllAsync()
    {
        if (!RequireCatalog())
            return;
        if (!await EnsureStartToolsAsync().ConfigureAwait(false))
            return;
        var catalog = Catalog!;
        var runtime = Runtime!;
        var health = new Dictionary<string, bool>(Health);
        var planned = ProcessSupervisor.OfflineRunnable(catalog, health);
        var plan = ServiceStartPlanner.ForTargets(catalog, planned.Select(s => s.Id));
        MarkServiceActivity(plan.Order.Count > 0 ? plan.Order : planned, ServiceActivityMap.Starting);
        IReadOnlyList<(string Id, string Label, string? Error)> results = [];
        await RunJobAsync("啟動中…", async () =>
        {
            results = await ProcessSupervisor.StartTargetsAsync(
                catalog,
                runtime,
                planned.Select(s => s.Id),
                onStatus: status =>
                {
                    JobText = status;
                    Notify();
                }).ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);
        ApplyStartResults(results);
        if (results.Count == 0 && !JobBusy)
            JobText = "所有服務已在線";
        Notify();
    }

    public async Task StartGroupAsync(string key)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        var runtime = Runtime!;
        var health = new Dictionary<string, bool>(Health);
        var ids = GroupRunnableIds(key);
        var planned = ProcessSupervisor.OfflineRunnable(catalog, health, ids);
        if (planned.Count == 0)
        {
            if (!JobBusy)
                JobText = "此群組服務已在線";
            Notify();
            return;
        }
        var plan = ServiceStartPlanner.ForTargets(catalog, planned.Select(s => s.Id));
        var toStart = plan.Order.Count > 0 ? plan.Order : planned;
        if (!await EnsureStartToolsAsync(toStart).ConfigureAwait(false))
            return;
        MarkServiceActivity(toStart, ServiceActivityMap.Starting);
        IReadOnlyList<(string Id, string Label, string? Error)> results = [];
        await RunJobAsync("啟動中…", async () =>
        {
            results = await ProcessSupervisor.StartTargetsAsync(
                catalog,
                runtime,
                planned.Select(s => s.Id),
                onStatus: status =>
                {
                    JobText = status;
                    Notify();
                }).ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);
        ApplyStartResults(results);
        Notify();
    }

    public Task StopGroupAsync(string key)
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var runtime = Runtime!;
        var targets = ProcessSupervisor.OnlineRunnable(catalog, Health, GroupRunnableIds(key));
        MarkServiceActivity(targets, ServiceActivityMap.Stopping);
        return RunJobAsync("停止中…", () =>
        {
            ProcessSupervisor.StopServices(catalog, runtime, targets);
            return Task.FromResult<string?>(null);
        });
    }

    public async Task RestartGroupAsync(string key)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        var runtime = Runtime!;
        var health = new Dictionary<string, bool>(Health);
        var ids = GroupRunnableIds(key);
        var targets = ProcessSupervisor.OnlineRunnable(catalog, health, ids);
        if (targets.Count == 0)
            return;
        if (!await EnsureStartToolsAsync(targets).ConfigureAwait(false))
            return;
        MarkServiceActivity(targets, ServiceActivityMap.Restarting);
        await RunJobAsync("重啟中…", () =>
        {
            var results = ProcessSupervisor.RestartOnline(catalog, runtime, health, ids);
            ApplyStartResults(results);
            return Task.FromResult<string?>(null);
        }).ConfigureAwait(false);
    }

    public void OpenGroupUrls(string key)
    {
        var node = ServiceGroupTree.Find(ServiceGroupRoots, key);
        if (node is null)
            return;
        var targets = node.Descendants().Where(s => !string.IsNullOrEmpty(s.OpenUrl)).ToList();
        MarkServiceActivity(targets, ServiceActivityMap.Opening, withHosted: false);
        var opened = 0;
        foreach (var svc in targets)
        {
            CliUtil.OpenUrl(svc.OpenUrl);
            opened++;
        }
        if (opened == 0)
            _native.Info("無 URL", "沒有可開啟的 openUrl。");
    }

    public async Task StartOneAsync(ServiceEntry svc, bool skipOptional = false, bool skipDepends = false)
    {
        if (!RequireCatalog())
            return;
        if (IsSelfService(svc))
        {
            _native.Info("本機控制台", ProcessSupervisor.SelfConsoleStartMessage);
            return;
        }
        if (!string.IsNullOrEmpty(svc.HostedBy))
        {
            _native.Info("隨宿主啟動", $"「{svc.Label}」隨 {svc.HostedBy} 一併提供，請啟動宿主服務。");
            return;
        }
        if (Health.GetValueOrDefault(svc.Id))
        {
            ApplyStartResults([(svc.Id, svc.Label, null)]);
            Notify();
            _native.Info("已在線", $"「{svc.Label}」已在執行。");
            return;
        }
        if (skipDepends && svc.Dependencies.Any(d => !d.Optional))
        {
            var hard = string.Join("、", svc.Dependencies.Where(d => !d.Optional).Select(d => d.Id));
            if (!_native.Confirm(
                    "只起自己",
                    $"「{svc.Label}」硬相依 {hard}。略過後可能無法連線。仍要只起自己嗎？"))
                return;
        }
        var catalog = Catalog!;
        var runtime = Runtime!;
        var plan = ServiceStartPlanner.ForTargets(catalog, [svc.Id], skipOptional, skipDepends);
        var toStart = plan.Order.Count > 0 ? plan.Order : [svc];
        if (!await EnsureStartToolsAsync(toStart).ConfigureAwait(false))
            return;
        MarkServiceActivity(toStart, ServiceActivityMap.Starting);
        await RunJobAsync($"啟動 {svc.Label}…", async () =>
        {
            var results = await ProcessSupervisor.StartTargetsAsync(
                catalog,
                runtime,
                [svc.Id],
                skipOptional: skipOptional,
                skipDepends: skipDepends,
                onStatus: status =>
                {
                    JobText = status;
                    Notify();
                }).ConfigureAwait(false);
            ApplyStartResults(results);
            var self = results.LastOrDefault(r => string.Equals(r.Id, svc.Id, StringComparison.OrdinalIgnoreCase));
            return self.Error;
        }).ConfigureAwait(false);
    }

    public Task StopAllAsync()
    {
        if (!RequireCatalog())
            return Task.CompletedTask;
        var catalog = Catalog!;
        var runtime = Runtime!;
        MarkServiceActivity(ProcessSupervisor.OnlineRunnable(catalog, Health), ServiceActivityMap.Stopping);
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
        if (IsSelfService(svc))
        {
            _native.Info("本機控制台", "這是目前這個控制台，停止請用右上角「離開」。");
            return Task.CompletedTask;
        }
        var catalog = Catalog!;
        var runtime = Runtime!;
        MarkServiceActivity([svc], ServiceActivityMap.Stopping);
        return RunJobAsync($"停止 {svc.Label}…", () =>
        {
            ProcessSupervisor.StopService(catalog, runtime, svc);
            return Task.FromResult<string?>(null);
        });
    }

    public async Task RestartOneAsync(ServiceEntry svc)
    {
        if (!RequireCatalog())
            return;
        if (IsSelfService(svc))
        {
            _native.Info("本機控制台", ProcessSupervisor.SelfConsoleStartMessage);
            return;
        }
        var catalog = Catalog!;
        var runtime = Runtime!;
        var plan = ServiceStartPlanner.ForTargets(catalog, [svc.Id]);
        var toStart = plan.Order.Count > 0 ? plan.Order : [svc];
        if (!await EnsureStartToolsAsync(toStart).ConfigureAwait(false))
            return;
        MarkServiceActivity(toStart, ServiceActivityMap.Restarting);
        await RunJobAsync($"重啟 {svc.Label}…", async () =>
        {
            ProcessSupervisor.StopService(catalog, runtime, svc);
            await Task.Delay(800).ConfigureAwait(false);
            var results = await ProcessSupervisor.StartTargetsAsync(
                catalog,
                runtime,
                [svc.Id],
                onStatus: status =>
                {
                    JobText = status;
                    Notify();
                }).ConfigureAwait(false);
            ApplyStartResults(results);
            var self = results.LastOrDefault(r => string.Equals(r.Id, svc.Id, StringComparison.OrdinalIgnoreCase));
            return self.Error;
        }).ConfigureAwait(false);
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
        MarkServiceActivity(targets, ServiceActivityMap.Opening, withHosted: false);
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
        if (string.IsNullOrEmpty(svc.OpenUrl))
            return;
        MarkServiceActivity([svc], ServiceActivityMap.Opening, withHosted: false);
        CliUtil.OpenUrl(svc.OpenUrl);
    }

    public void Doctor()
    {
        AskPanelOpen = false;
        AuditPanelOpen = false;
        DoctorView = DoctorSnapshot.Build(Catalog);
        DoctorCopied = false;
        Dialog = "doctor";
        Notify();
        _ = RefreshGitStatusThenNotifyAsync();
    }

    public async Task CopyDoctorAsync()
    {
        if (DoctorView is null || Js is null)
            return;
        try
        {
            await Js.InvokeVoidAsync("aiConsole.copyText", DoctorView.ToText()).ConfigureAwait(false);
            DoctorCopied = true;
            JobText = "已複製環境體檢報告。";
        }
        catch
        {
            DoctorCopied = false;
            JobText = "無法複製到剪貼簿。";
        }
        Notify();
    }

    public void OpenDoctorSettings(string tab)
    {
        OpenPreferences(tab);
    }

    public async Task PickProjectFromDoctorAsync()
    {
        CloseDialog();
        await PickProjectAsync().ConfigureAwait(false);
    }

    public async Task RunDoctorActionAsync(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) || JobBusy)
            return;
        if (actionId == "install-docfx")
        {
            await InstallDocfxFromDoctorAsync().ConfigureAwait(false);
            return;
        }
        if (actionId == "install-toolchains")
        {
            await InstallMissingToolchainsAsync().ConfigureAwait(false);
            return;
        }
        const string installPrefix = "install-toolchain:";
        if (actionId.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await InstallToolchainAsync(actionId[installPrefix.Length..]).ConfigureAwait(false);
            return;
        }
        const string restorePrefix = "restore-packages:";
        if (actionId.StartsWith(restorePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await RestorePackagesAsync(actionId[restorePrefix.Length..]).ConfigureAwait(false);
            return;
        }
    }

    public async Task InstallMissingToolchainsAsync()
    {
        if (Catalog is null)
            return;
        var missing = ToolchainBootstrap.MissingTools(TechStackCatalog.RequiredToolIdsFor(TechStackDetector.RequiredStacks(Catalog)));
        if (missing.Count == 0)
        {
            _native.Info("開發環境", "目前偵測到的語言環境都已安裝。");
            return;
        }
        var names = string.Join("、", missing.Select(m => m.DisplayName));
        if (!_native.Confirm("安裝開發環境", $"將安裝：{names}。\n\nWindows 會用 winget；沒有 winget 則開啟官方下載頁。確定？"))
            return;
        foreach (var spec in missing)
            await InstallToolchainAsync(spec.Id, confirm: false).ConfigureAwait(false);
    }

    public async Task InstallToolchainAsync(string toolId, bool confirm = true)
    {
        var spec = TechStackCatalog.Tool(toolId);
        if (spec is null)
            return;
        if (confirm && !_native.Confirm("安裝 " + spec.DisplayName, spec.HowTo + "\n\n現在安裝？"))
            return;
        if (JobBusy)
            return;
        JobBusy = true;
        JobText = "安裝 " + spec.DisplayName + "…";
        Notify();
        try
        {
            var progress = new Progress<string>(line =>
            {
                JobText = spec.DisplayName + "：" + Truncate(line, 80);
                Notify();
            });
            var (code, output) = await ToolchainBootstrap.InstallAsync(spec.Id, progress, _cts.Token).ConfigureAwait(false);
            JobText = code == 0 ? spec.DisplayName + " 已就緒" : "安裝未完成";
            if (code != 0)
                _native.Warn("安裝 " + spec.DisplayName, FirstLine(output));
        }
        catch (Exception ex)
        {
            JobText = "錯誤";
            _native.Error("安裝 " + spec.DisplayName, FirstLine(ex.Message));
        }
        finally
        {
            JobBusy = false;
            if (Dialog == "doctor")
                DoctorView = DoctorSnapshot.Build(Catalog);
            Notify();
        }
    }

    public async Task RestorePackagesAsync(string relDir)
    {
        if (Catalog is null || JobBusy)
            return;
        var target = Path.Combine(Catalog.Root, relDir.Replace('/', Path.DirectorySeparatorChar));
        JobBusy = true;
        JobText = "還原套件…";
        Notify();
        try
        {
            var plan = StackCommands.PlanRestore(Catalog.Root, target);
            var progress = new Progress<string>(line =>
            {
                JobText = Truncate(line, 80);
                Notify();
            });
            var (code, log) = await StackCommands.RunAsync(plan, progress, _cts.Token).ConfigureAwait(false);
            JobText = code == 0 ? "套件已還原" : "還原失敗";
            if (code != 0)
                _native.Warn("還原套件", FirstLine(log));
        }
        catch (Exception ex)
        {
            JobText = "錯誤";
            _native.Error("還原套件", FirstLine(ex.Message));
        }
        finally
        {
            JobBusy = false;
            if (Dialog == "doctor")
                DoctorView = DoctorSnapshot.Build(Catalog);
            Notify();
        }
    }

    static string Truncate(string text, int max)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    public async Task InstallDocfxFromDoctorAsync()
    {
        if (Catalog is null || JobBusy)
            return;
        JobBusy = true;
        JobText = "安裝 DocFX…";
        Notify();
        try
        {
            var msg = await DocsService.InstallDocfxAsync(Catalog.Root).ConfigureAwait(false);
            JobText = msg;
        }
        catch (Exception ex)
        {
            JobText = "錯誤";
            _native.Error("安裝 DocFX", FirstLine(ex.Message));
        }
        finally
        {
            JobBusy = false;
            if (Dialog == "doctor")
                DoctorView = DoctorSnapshot.Build(Catalog);
            Notify();
        }
    }

    public async Task CheckUpdateAsync()
    {
        var ans = _native.YesNoCancel(
            "檢查更新",
            "是否包含 RC／預發行版本？\n\n「是」＝正式版與 RC 都看，取較新者\n「否」＝只看正式版\n「取消」＝不檢查");
        if (ans == PhotinoDialogResult.Cancel)
            return;
        var includeRc = ans == PhotinoDialogResult.Yes;
        if (Dialog == "prefs")
            CloseDialog();
        await RunJobAsync("檢查更新…", async () =>
        {
            var update = await SelfUpdate.CheckLatestAsync(_cts.Token, includePrerelease: includeRc).ConfigureAwait(false);
            ConsoleSettingsStore.MarkUpdateChecked();
            if (update is null)
            {
                UpdateAvailable = null;
                return includeRc
                    ? $"目前已是最新版本 v{AppInfo.Version}（含 RC）。"
                    : $"目前已是最新版本 v{AppInfo.Version}。";
            }
            UpdateAvailable = update;
            Notify();
            var rc = update.Prerelease ? "（RC／預發行）" : "";
            return $"發現新版本 {update.Tag}{rc}（目前 v{AppInfo.Version}）。可按「立即更新」下載並安裝。";
        }, refreshBuilds: false);
        if (UpdateAvailable is null && !JobBusy && JobText != "錯誤")
        {
            _native.Info(
                "檢查更新",
                includeRc
                    ? $"目前已是最新版本 v{AppInfo.Version}（含 RC）。"
                    : $"目前已是最新版本 v{AppInfo.Version}。");
        }
    }

    public async Task ApplyUpdateFromFileAsync()
    {
        if (Dialog == "prefs")
            CloseDialog();
        await Task.Yield();

        string[]? files;
        try
        {
            files = await _native.PickFilesAsync(
                "選擇更新檔",
                ("安裝包／壓縮檔", [".zip", ".exe"]),
                ("所有檔案", ["*"])).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _native.Error("無法選擇檔案", ex.Message);
            return;
        }
        if (files is null || files.Length == 0 || string.IsNullOrWhiteSpace(files[0]))
            return;
        var path = files[0];

        var kind = SelfUpdate.DetectInstallKind();
        var mode = SelfUpdate.ResolveLocalFileMode(path, kind);
        if (mode is UpdateApplyMode.None or UpdateApplyMode.OpenReleases)
        {
            _native.Info(
                kind == InstallKind.Development ? "無法自動覆蓋" : "無法套用此檔案",
                SelfUpdate.CannotApplyLocalFileHint(kind, path));
            return;
        }

        if (!_native.Confirm(
            "從檔案更新",
            (kind == InstallKind.Development
                ? "目前是從原始碼／開發目錄執行。套用會覆蓋目前執行目錄，不會改 Git 原始碼。\n\n"
                : "") +
            $"將用本機檔案安裝：\n{path}\n\n會開啟安裝程式。安裝時控制台可能會關閉並在完成後重開。\n已啟動的專案服務不會自動停止。\n\n確定更新？"))
            return;

        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return;
        }

        JobBusy = true;
        JobText = "套用更新…";
        Notify();
        try
        {
            var fromInstaller = mode == UpdateApplyMode.Installer;
            SelfUpdate.LaunchApply(path, mode, silent: !fromInstaller);
            if (fromInstaller)
            {
                JobBusy = false;
                JobText = "安裝程式已開啟";
                Notify();
                return;
            }
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
            _native.Info(
                kind == InstallKind.Development ? "無法自動覆蓋" : "找不到安裝包",
                SelfUpdate.CannotApplyHint(update, kind));
            return;
        }

        var asset = mode == UpdateApplyMode.Installer ? update.SetupAsset : update.ZipAsset;
        if (asset is null)
        {
            SelfUpdate.OpenReleases(update.HtmlUrl);
            _native.Info("找不到安裝包", SelfUpdate.CannotApplyHint(update, kind));
            return;
        }

        if (!_native.Confirm(
            "更新控制台",
            $"將下載並安裝 {update.Tag}{(update.Prerelease ? "（RC／預發行）" : "")}（目前 v{AppInfo.Version}）。\n控制台會先關閉以便覆蓋檔案，安裝完成後會自動重開。\n已啟動的專案服務不會自動停止。\n\n確定更新？"))
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
                ShowInfoReport(DeployConfigResolver.StatusView(Catalog));
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
                ShowInfoReport(DeployConfigResolver.CiHintView(Catalog));
                return;
            case "github_clone":
                OpenCloneDialog();
                return;
            case "github_settings":
                await EditGithubAsync().ConfigureAwait(false);
                return;
            case "github_status":
                await OpenGithubStatusAsync().ConfigureAwait(false);
                return;
            case "github_switch_branch":
                await OpenBranchDialogAsync().ConfigureAwait(false);
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
                await PublishCurrentBranchAsync().ConfigureAwait(false);
                return;
            case "github_pr":
                await OpenOrCreatePrAsync().ConfigureAwait(false);
                return;
            case "github_ci_scaffold":
                await ScaffoldCiWorkflowAsync(alreadyConfirmed: true).ConfigureAwait(false);
                return;
            case "github_actions":
                await OpenCiDialogAsync().ConfigureAwait(false);
                return;
            case "github_release":
                await OpenReleaseDialogAsync().ConfigureAwait(false);
                return;
            case "github_releases":
                await OpenReleaseListAsync().ConfigureAwait(false);
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
            case "build_test":
                await RunTestsAsync(asGate: false).ConfigureAwait(false);
                return;
            case "docs_scaffold":
                await ScaffoldDocsAsync().ConfigureAwait(false);
                return;
            case "docs_open_folder":
                OpenDocsFolder();
                return;
            case "docs_ai_fill":
                await AiFillDocsAsync(currentOnly: false).ConfigureAwait(false);
                return;
            case "docs_serve":
                await ServeDocsAsync().ConfigureAwait(false);
                return;
            case "docs_open_pages":
                await OpenDocsPagesAsync().ConfigureAwait(false);
                return;
            case "docs_enable_pages":
                await EnableDocsPagesAsync().ConfigureAwait(false);
                return;
            case "docs_publish_pages":
                await PublishDocsPagesAsync().ConfigureAwait(false);
                return;
        }
    }

    public async Task BuildOneAsync(string relPath)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        var target = Path.Combine(catalog.Root, relPath.Replace('/', Path.DirectorySeparatorChar));
        if (!await EnsureBuildToolsAsync([target]).ConfigureAwait(false))
            return;
        LeftTab = "prj";
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Notify();
        await RunJobAsync($"編譯 {Path.GetFileName(relPath)}…", async () =>
        {
            await BeginBuildBatchAsync([target]).ConfigureAwait(false);
            MarkBuildActivity(target, "building");
            AppendBuild($"=== build {relPath} ===");
            var progress = new Progress<string>(AppendBuild);
            var (code, log) = await BuildRunner.BuildAsync(catalog.Root, target, progress).ConfigureAwait(false);
            AppendBuild($"exit {code}");
            FinishOneBuild(target, code);
            if (code != 0)
                RememberBuildFailure(relPath, code, $"=== build {relPath} ===\n{log}\nexit {code}");
            JobText = BuildProgressText;
            return (string?)null;
        }, refreshBuilds: false).ConfigureAwait(false);
        await RefreshBuildStatesAsync(clearActivity: false).ConfigureAwait(false);
    }

    public Task OpenBranchDialogAsync() => OpenBranchDialogAsync(null);

    public async Task OpenBranchDialogAsync(string? suggestedName)
    {
        if (!RequireCatalog())
            return;
        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return;
        }
        NewBranchName = (suggestedName ?? "").Trim();
        BranchDialogHint = _resumeIssueAfterBranch is not null
            ? "為此任務建立功能分支。未提交的改動會跟著走；建立後請提交、發布，再回到任務視窗按「建立 PR」。"
            : "";
        Dialog = "branch";
        Notify();
        await RefreshBranchListAsync().ConfigureAwait(false);
    }

    public async Task SwitchToBranchAsync(GitBranchInfo branch)
    {
        if (Catalog is null || branch.IsCurrent)
            return;
        await RunBranchChangeAsync(
            $"切換到 {branch.LocalName}…",
            () => GitHubService.SwitchBranchAsync(Catalog.Root, branch.Name)).ConfigureAwait(false);
    }

    public async Task CreateBranchAsync()
    {
        if (Catalog is null)
            return;
        var name = NewBranchName.Trim();
        if (!GitHubService.IsValidBranchName(name))
        {
            BranchDialogHint = "請填寫有效的分支名稱，例如 feat/login。";
            Notify();
            return;
        }
        await RunBranchChangeAsync(
            $"建立 {name}…",
            () => GitHubService.CreateBranchAsync(Catalog.Root, name)).ConfigureAwait(false);
    }

    private async Task RunBranchChangeAsync(string title, Func<Task<string>> action)
    {
        if (Catalog is null)
            return;
        await RunJobAsync(title, async () =>
        {
            var msg = await action().ConfigureAwait(false);
            try
            {
                var brief = await GitHubService.TryBriefStatusAsync(Catalog.Root).ConfigureAwait(false);
                if (brief is { HasUpstream: true } && await GitHubService.HasRemoteAsync(Catalog.Root).ConfigureAwait(false))
                    return msg + "\n\n" + await GitHubService.SyncFromRemoteAsync(Catalog).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return msg + "\n\n已切換，但同步失敗：\n" + FirstLine(ex.Message);
            }
            return msg;
        }).ConfigureAwait(false);
        await RefreshGitStatusAsync().ConfigureAwait(false);
        await RefreshBuildStatesAsync().ConfigureAwait(false);
        NewBranchName = "";
        if (_resumeIssueAfterBranch is { } issue)
        {
            _resumeIssueAfterBranch = null;
            Dialog = "issue";
            if (ActiveIssue is null || ActiveIssue.Number != issue.Number)
                OpenIssueDialog(issue);
            else
                Notify();
            return;
        }
        if (Dialog == "branch")
            CloseDialog();
    }

    private async Task RefreshBranchListAsync()
    {
        if (Catalog is null)
        {
            BranchList = [];
            return;
        }
        try
        {
            BranchDialogHint = "讀取分支…";
            Notify();
            BranchList = await GitHubService.ListBranchesAsync(Catalog.Root).ConfigureAwait(false);
            BranchDialogHint = BranchList.Count == 0
                ? "找不到分支。"
                : HasUncommitted
                    ? "工作區有未提交變更，必須先提交或還原才能切換。"
                    : "點選分支即可切換。遠端才有的分支會自動建立本機追蹤。";
        }
        catch (Exception ex)
        {
            BranchList = [];
            BranchDialogHint = FirstLine(ex.Message);
        }
        Notify();
    }

    public void RefreshDocsFromUi()
    {
        RefreshDocs(keepSelection: true);
        JobText = Docs is null ? "已刷新文件" : "文件 " + Docs.Label();
        Notify();
    }

    public void RefreshDocs(bool keepSelection = false)
    {
        if (Catalog is null)
        {
            ClearDocsState();
            Notify();
            return;
        }
        var previous = keepSelection ? SelectedDocPath : null;
        Docs = DocsService.Scan(Catalog.Root);
        DocsHint = Docs.Health switch
        {
            DocsHealth.Missing => "還沒有 docs/。按「建立／補齊體系」產生標準骨架。",
            DocsHealth.Incomplete => "有文件但缺 toc.yml 或 docfx.json。可再按「建立／補齊體系」。",
            DocsHealth.Draft => $"有 {Docs.StubCount} 頁仍標待補。可用 AI 補齊或在右側編輯。",
            _ => Docs.HasWorkflow ? "文件就緒。可本機預覽或發布到 GitHub Pages。" : "文件就緒。尚未放 Pages workflow，發布前會自動補上。",
        };
        if (previous is not null && Docs.Files.Any(f => f.RelPath == previous))
        {
            if (!DocsDirty)
                LoadDoc(previous);
            Notify();
            return;
        }
        SelectedDocPath = null;
        DocsDraft = "";
        DocsDirty = false;
        var first = Docs.Files.FirstOrDefault(f => !f.IsConfig) ?? Docs.Files.FirstOrDefault();
        if (first is not null)
            LoadDoc(first.RelPath);
        Notify();
    }

    public void SetDocsDraft(string value)
    {
        DocsDraft = value ?? "";
        DocsDirty = true;
        Notify();
    }

    public async Task SelectDocAsync(string relPath)
    {
        if (!ConfirmDiscardDocs())
            return;
        LoadDoc(relPath);
        LeftTab = "docs";
        Notify();
        await Task.CompletedTask;
    }

    public void SaveSelectedDoc()
    {
        if (!RequireCatalog() || string.IsNullOrEmpty(SelectedDocPath))
            return;
        try
        {
            DocsService.Write(Catalog!.Root, SelectedDocPath, DocsDraft);
            DocsDirty = false;
            Docs = DocsService.Scan(Catalog.Root);
            DocsHint = "已儲存 " + SelectedDocPath;
            JobText = "已儲存文件";
            Notify();
        }
        catch (Exception ex)
        {
            _native.Error("無法儲存文件", ex.Message);
        }
    }

    public async Task ScaffoldDocsAsync()
    {
        if (!RequireCatalog())
            return;
        if (!_native.Confirm(
            "建立文件體系",
            "將在 docs/ 建立標準 Markdown 骨架與 DocFX／Pages 設定。已有的檔不會覆蓋。確定？"))
            return;
        await RunJobAsync("建立文件體系…", async () =>
        {
            var ctx = DocsScaffoldContext.FromCatalog(Catalog);
            try
            {
                var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cfg.Slug()))
                    ctx = ctx with { GithubSlug = cfg.Slug() };
            }
            catch
            {
                // slug optional
            }
            var result = DocsService.Scaffold(Catalog!.Root, ctx);
            return result.Message;
        }).ConfigureAwait(false);
        RefreshDocs();
        ShowDocs();
    }

    public async Task AiFillDocsAsync(bool currentOnly)
    {
        if (!RequireCatalog())
            return;
        if (!ConfirmDiscardDocs())
            return;
        RefreshDocs(keepSelection: true);
        var ctx = DocsScaffoldContext.FromCatalog(Catalog);
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(cfg.Slug()))
                ctx = ctx with { GithubSlug = cfg.Slug() };
        }
        catch
        {
            // optional
        }
        string prompt;
        if (currentOnly && !string.IsNullOrEmpty(SelectedDocPath))
            prompt = DocsPrompts.FillOne(SelectedDocPath, DocsDraft, ctx);
        else
            prompt = DocsPrompts.FillAll(Docs ?? DocsService.Scan(Catalog!.Root), ctx);
        AgentPrompt = prompt;
        AgentTitle = currentOnly ? "AI 補齊本頁" : "AI 補齊文件";
        AgentIntro = AgentLaunchIntro();
        Dialog = "agent";
        Notify();
    }

    public async Task ServeDocsAsync()
    {
        if (!RequireCatalog())
            return;
        if (DocsServing)
        {
            OpenDocsPreviewPage();
            return;
        }
        await RunJobAsync("啟動文件網站預覽…", async () =>
        {
            var handle = await DocsService.ServeAsync(Catalog!.Root, readyTimeoutMs: 180_000).ConfigureAwait(false);
            _docsServe = handle;
            var pageUrl = DocsService.PreviewUrl(Catalog.Root, SelectedDocPath, handle.Url);
            CliUtil.OpenUrl(pageUrl);
            return DocsPreviewOpensCurrentPage
                ? "本機文件站（目前這頁）：" + pageUrl
                : "本機文件站（首頁）：" + pageUrl;
        }).ConfigureAwait(false);
        Notify();
    }

    public void OpenNewDocDialog(string? folder = null)
    {
        if (!RequireCatalog())
            return;
        var prefix = folder;
        if (string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(SelectedDocPath))
        {
            var slash = SelectedDocPath.LastIndexOf('/');
            prefix = slash >= 0 ? SelectedDocPath[..slash] : "";
        }
        NewDocPath = string.IsNullOrEmpty(prefix) ? "user/new-page.md" : prefix.TrimEnd('/') + "/new-page.md";
        NewDocHint = "路徑相對於 docs/，例如 user/new-page.md。資料夾不存在會自動建立。";
        Dialog = "new-doc";
        Notify();
    }

    public void CreateNewDoc()
    {
        if (!RequireCatalog())
            return;
        var rel = (NewDocPath ?? "").Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(Path.GetExtension(rel)))
            rel += ".md";
        if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            NewDocHint = "新增文件請用 .md。";
            Notify();
            return;
        }
        if (!DocsService.IsSafeRelPath(rel))
        {
            NewDocHint = "路徑無效。只能用 docs/ 內的相對路徑。";
            Notify();
            return;
        }
        if (!ConfirmDiscardDocs())
            return;
        try
        {
            var existing = DocsService.Read(Catalog!.Root, rel);
            if (string.IsNullOrEmpty(existing))
                DocsService.Write(Catalog.Root, rel, DocsService.NewPageStub(rel));
            var folder = rel.Contains('/') ? rel[..rel.LastIndexOf('/')] : "";
            if (!string.IsNullOrEmpty(folder))
                _collapsedDocFolders.Remove(folder);
            Docs = DocsService.Scan(Catalog.Root);
            LoadDoc(rel);
            LeftTab = "docs";
            DocsHint = string.IsNullOrEmpty(existing) ? "已新增 " + rel : "已開啟既有檔 " + rel;
            JobText = DocsHint;
            CloseDialog();
        }
        catch (Exception ex)
        {
            NewDocHint = FirstLine(ex.Message);
            Notify();
        }
    }

    public void DeleteDoc(string relPath)
    {
        if (!RequireCatalog() || string.IsNullOrWhiteSpace(relPath))
            return;
        var rel = relPath.Trim().Replace('\\', '/').Trim('/');
        if (!DocsService.IsSafeRelPath(rel))
        {
            _native.Warn("無法刪除", "路徑無效。只能刪 docs/ 內的 Markdown 或 DocFX 設定。");
            return;
        }
        var extra = DocsService.IsScaffoldFile(rel)
            ? "這是骨架檔，之後可用「建立體系」再產生（不會覆蓋你已改過的其他檔）。\n\n"
            : "";
        if (!_native.Confirm("刪除文件", extra + $"確定刪除 docs/{rel}？本機檔案會立刻移除。"))
            return;
        try
        {
            if (!DocsService.Delete(Catalog!.Root, rel))
            {
                _native.Warn("無法刪除", "找不到這個檔，或已經不在磁碟上。");
                RefreshDocs(keepSelection: true);
                return;
            }
            var wasSelected = string.Equals(SelectedDocPath, rel, StringComparison.OrdinalIgnoreCase);
            if (wasSelected)
            {
                SelectedDocPath = null;
                DocsDraft = "";
                DocsDirty = false;
            }
            Docs = DocsService.Scan(Catalog.Root);
            DocsHint = "已刪除 " + rel;
            JobText = DocsHint;
            if (wasSelected)
            {
                var first = Docs.Files.FirstOrDefault(f => !f.IsConfig) ?? Docs.Files.FirstOrDefault();
                if (first is not null)
                    LoadDoc(first.RelPath);
            }
            Notify();
        }
        catch (Exception ex)
        {
            _native.Error("無法刪除文件", ex.Message);
        }
    }

    void OpenDocsPreviewPage()
    {
        var pageUrl = DocsPreviewPageUrl;
        CliUtil.OpenUrl(pageUrl);
        JobText = DocsPreviewOpensCurrentPage
            ? "文件網站預覽（目前這頁）：" + pageUrl
            : "文件網站預覽（首頁）：" + pageUrl;
        Notify();
    }

    public async Task OpenDocsPagesAsync()
    {
        if (!RequireCatalog())
            return;
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            var status = await DocsService.PagesStatusAsync(Catalog!, cfg).ConfigureAwait(false);
            var url = status.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? status.Split('\n')[0].Trim()
                : DocsService.PagesUrl(cfg);
            if (!DocsService.OpenPages(cfg, url))
                _native.Info("無法開啟", "請先完成 GitHub 設定（owner/repo），或先啟用 GitHub Pages。");
            else
                JobText = "已開啟線上文件";
            Notify();
        }
        catch (Exception ex)
        {
            _native.Warn("線上文件", FirstLine(ex.Message));
        }
    }

    public async Task EnableDocsPagesAsync()
    {
        if (!RequireCatalog())
            return;
        await RunJobAsync("啟用 GitHub Pages…", async () =>
            await DocsService.EnablePagesAsync(Catalog!).ConfigureAwait(false)).ConfigureAwait(false);
        RefreshDocs(keepSelection: true);
    }

    public async Task PublishDocsPagesAsync()
    {
        if (!RequireCatalog())
            return;
        DocsService.EnsureWorkflow(Catalog!.Root);
        DocsService.EnsureToolsManifest(Catalog.Root);
        await RunJobAsync("發布中…", async () =>
        {
            var push = await GitHubService.PublishBranchAsync(Catalog!).ConfigureAwait(false);
            BeginWatchCiAfterPush();
            return push + "\n\n已開始監看 Actions。摘要列會更新 CI 狀態。";
        }).ConfigureAwait(false);
        await RefreshGitStatusAsync().ConfigureAwait(false);
        RefreshDocs(keepSelection: true);
    }

    public void OpenDocsFolder()
    {
        if (!RequireCatalog())
            return;
        var dir = DocsService.DocsDirectory(Catalog!.Root);
        Directory.CreateDirectory(dir);
        CliUtil.OpenPath(dir);
    }

    public void StopDocsServe()
    {
        _docsServe?.Dispose();
        _docsServe = null;
    }

    private void LoadDoc(string relPath)
    {
        if (Catalog is null)
            return;
        try
        {
            SelectedDocPath = relPath;
            DocsDraft = DocsService.Read(Catalog.Root, relPath);
            DocsDirty = false;
        }
        catch (Exception ex)
        {
            DocsHint = FirstLine(ex.Message);
        }
    }

    private bool ConfirmDiscardDocs()
    {
        if (!DocsDirty)
            return true;
        return _native.Confirm("尚未儲存", "這頁有未儲存的修改。要放棄並切換嗎？");
    }

    private void ClearDocsState()
    {
        StopDocsServe();
        Docs = null;
        SelectedDocPath = null;
        DocsDraft = "";
        DocsDirty = false;
        DocsPreviewMode = false;
        DocsHint = "";
        NewDocPath = "";
        NewDocHint = "";
        _collapsedDocFolders.Clear();
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
        var oldTag = ReleaseTag;
        if (string.IsNullOrWhiteSpace(ReleaseTitle)
            || ReleaseTitle == oldTag
            || ReleaseTitle == oldTag + " " + AppInfo.Product)
            ReleaseTitle = ReleasePackable ? next + " " + AppInfo.Product : next;
        ReleaseTag = next;
        AttachReleaseDistAssets();
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

    public async Task PackReleaseAssetsAsync()
    {
        if (Catalog is null || !ReleasePackable)
            return;
        var tag = ReleaseTag.Trim();
        if (!ReleaseVersion.IsValidTag(tag))
        {
            _native.Warn("版號無效", "請先填寫版號／Tag，例如 v1.2.3。");
            return;
        }
        PackedReleaseAssets? packed = null;
        var ok = await RunReleaseProgressAsync(
            ReleaseRunState.PackOnly(),
            returnDialog: "release",
            fn: async progress =>
            {
                packed = await ConsoleReleasePack.PackAsync(Catalog.Root, tag, progress, _cts.Token).ConfigureAwait(false);
                return packed.HasSetup
                    ? "已附上 " + Path.GetFileName(packed.SetupPath) + "。"
                    : "打包完成。";
            }).ConfigureAwait(false);
        if (!ok || packed is null)
            return;
        foreach (var p in packed.ExistingPaths())
        {
            if (!ReleaseAssets.Contains(p))
                ReleaseAssets.Add(p);
        }
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
        var packable = ReleasePackable;
        var draft = ReleaseDraft;
        var needsPack = ConsoleReleasePack.RequiresInstaller(packable, draft)
            && !ConsoleReleasePack.HasSetupAsset(ReleaseAssets);
        var title = string.IsNullOrWhiteSpace(ReleaseTitle) ? tag : ReleaseTitle.Trim();
        var kind = draft ? "草稿" : ReleasePrerelease ? "預發行" : "正式發行";
        var extra = ConsoleReleasePack.RequiresInstaller(packable, draft)
            ? "\n\n會附加 Windows 安裝包（沒有則先打包，並把版號寫進 AppInfo 等檔案）。畫面會顯示步驟與紀錄，編譯可能要數分鐘。已安裝使用者才能自動啟動安裝程式。"
            : "";
        if (!_native.Confirm("發行 Release", $"將在 GitHub 建立 Release（{kind}）：\n{tag}\n標題：{title}{extra}\n\n確定發行？"))
            return;
        var notes = ReleaseNotes;
        var assets = ReleaseAssets.ToList();
        var target = ReleaseTarget;
        var prerelease = ReleasePrerelease;
        var generateNotes = ReleaseGenerateNotes;
        var makeLatest = ReleaseMakeLatest;
        var run = needsPack ? ReleaseRunState.PackAndPublish() : ReleaseRunState.PublishOnly();
        var ok = await RunReleaseProgressAsync(run, returnDialog: null, fn: async progress =>
        {
            if (ConsoleReleasePack.RequiresInstaller(packable, draft))
            {
                if (!ConsoleReleasePack.HasSetupAsset(assets))
                {
                    var packed = await ConsoleReleasePack.PackAsync(Catalog.Root, tag, progress, _cts.Token).ConfigureAwait(false);
                    foreach (var p in packed.ExistingPaths())
                    {
                        if (!assets.Contains(p))
                            assets.Add(p);
                    }
                    notes = ConsoleReleasePack.MergeNotes(notes, packed);
                }
                else
                    notes = ConsoleReleasePack.MergeNotes(notes, ConsoleReleasePack.FromAssetPaths(tag, assets));
                if (!ConsoleReleasePack.HasSetupAsset(assets))
                    throw new InvalidOperationException("正式發行此控制台必須附加 *-win-x64-setup.exe，否則已安裝使用者的自動更新會改開 GitHub 頁。");
            }
            var req = new ReleaseRequest(
                Tag: tag,
                Title: title,
                Notes: notes,
                Target: target,
                Draft: draft,
                Prerelease: prerelease,
                GenerateNotes: generateNotes,
                MakeLatest: makeLatest,
                Assets: assets);
            return await GitHubService.PublishReleaseAsync(Catalog, req, progress: progress).ConfigureAwait(false);
        }).ConfigureAwait(false);
        if (ok)
            StampReleaseOnIntake(tag);
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
        CommitSelected.Clear();
        foreach (var c in changes)
            CommitSelected.Add(c.Path);
        CommitSubject = "";
        CommitBody = "";
        CommitMessage = "";
        CommitPushAfter = false;
        CommitSuggestHint = "";
        CommitFileQuery = "";
        CommitPreviewPath = null;
        CommitPreviewDiff = "";
        RefreshCommitHint();
        Dialog = "commit";
        Notify();
    }

    public bool IsCommitSelected(GitChange change) => CommitSelected.Contains(change.Path);

    public bool AllCommitSelected =>
        CommitChanges.Count > 0 && CommitChanges.All(c => CommitSelected.Contains(c.Path));

    public void ToggleCommitChange(GitChange change, bool selected)
    {
        if (selected)
            CommitSelected.Add(change.Path);
        else
            CommitSelected.Remove(change.Path);
        RefreshCommitHint();
        Notify();
    }

    public void SetCommitSelectionAll(bool selected)
    {
        CommitSelected.Clear();
        if (selected)
        {
            foreach (var c in CommitChanges)
                CommitSelected.Add(c.Path);
        }
        RefreshCommitHint();
        Notify();
    }

    public void SetCommitFileQuery(string value)
    {
        CommitFileQuery = value ?? "";
        Notify();
    }

    void RefreshCommitHint()
    {
        var branch = GitBrief?.Branch ?? "目前分支";
        CommitHint = $"分支 {branch} · {CommitChanges.Count} 筆異動 · 已選 {CommitSelected.Count} 筆將提交";
    }

    public async Task PreviewCommitChangeAsync(GitChange change)
    {
        if (!RequireCatalog())
            return;
        if (string.Equals(CommitPreviewPath, change.Path, StringComparison.Ordinal)
            && !string.IsNullOrEmpty(CommitPreviewDiff)
            && !CommitPreviewBusy)
        {
            CommitPreviewPath = null;
            CommitPreviewDiff = "";
            Notify();
            return;
        }
        CommitPreviewPath = change.Path;
        CommitPreviewBusy = true;
        CommitPreviewDiff = "";
        Notify();
        try
        {
            CommitPreviewDiff = await GitHubService.PreviewDiffAsync(Catalog!.Root, change).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CommitPreviewDiff = "無法預覽：" + FirstLine(ex.Message);
        }
        CommitPreviewBusy = false;
        Notify();
    }

    public async Task SuggestCommitMessageAsync()
    {
        if (!RequireCatalog())
            return;
        var selected = SelectedCommitChanges;
        if (selected.Count == 0)
        {
            _native.Warn("尚未選擇檔案", "請先勾選要提交的檔案，再產生說明。");
            return;
        }
        if ((!string.IsNullOrWhiteSpace(CommitSubject) || !string.IsNullOrWhiteSpace(CommitBody))
            && !_native.Confirm("取代說明", "將用 AI 建議覆蓋目前的標題與交付說明。確定？"))
            return;

        CommitSuggestion? suggestion = null;
        await RunJobAsync("AI 建議說明…", async () =>
        {
            suggestion = await CommitMessageSuggester.SuggestAsync(Catalog!.Root, selected).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.Message))
        {
            _native.Warn("無法產生建議", "請手動填寫提交說明。");
            return;
        }
        var (subject, body) = CommitMessageSuggester.SplitMessage(suggestion.Message);
        CommitSubject = subject;
        CommitBody = body;
        CommitMessage = suggestion.Message;
        CommitSuggestHint = suggestion.Hint;
        Dialog = "commit";
        Notify();
    }

    public async Task ConfirmCommitAsync()
    {
        if (!RequireCatalog())
            return;
        var selected = SelectedCommitChanges;
        if (selected.Count == 0)
        {
            _native.Warn("尚未選擇檔案", "請勾選要提交的檔案。未勾選的檔案會留在工作區。");
            return;
        }
        var message = CombinedCommitMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            _native.Warn("請填寫說明", "請填寫本次交付標題。");
            return;
        }
        var n = selected.Count;
        var skipped = CommitChanges.Count - n;
        var push = CommitPushAfter;
        var extra = skipped > 0 ? $"\n另有 {skipped} 筆異動不會提交。" : "";
        if (!_native.Confirm(
            "提交",
            $"將提交 {n} 筆變更到目前分支。{extra}{(push ? "\n提交後會再 push。" : "")}\n\n{CommitSubject}\n\n確定？"))
            return;
        CloseDialog();
        await RunJobAsync("提交中…", async () =>
            await GitHubService.CommitSelectedAsync(Catalog!.Root, message, selected).ConfigureAwait(false)).ConfigureAwait(false);
        await RefreshGitStatusAsync().ConfigureAwait(false);
        if (push && JobText != "錯誤")
            await PublishCurrentBranchAsync().ConfigureAwait(false);
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
        if (!RequireCatalog() || !HasUatTarget)
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
        await LaunchAgentAsync(prompt, AgentLaunchStatus("UAT 求救")).ConfigureAwait(false);
    }

    public void OpenCompileHelp()
    {
        LeftTab = "prj";
        if (!RequireCatalog() || LastBuildFailure is null)
        {
            _native.Info("編譯求救", "目前沒有建置錯誤可送出。");
            Notify();
            return;
        }
        AgentPrompt = CursorLauncher.BuildAgentPrompt(
            Catalog!.Root, LastBuildFailure.Target, LastBuildFailure.ExitCode, LastBuildFailure.Log);
        AgentTitle = "編譯求救";
        AgentIntro = AgentLaunchIntro();
        Dialog = "agent";
        Notify();
    }

    public void OpenRuntimeHelp()
    {
        LeftTab = "svc";
        if (!RequireCatalog())
            return;
        var svc = SelectedService();
        if (svc is null)
        {
            _native.Info("執行求救", "請先在服務列表選取一項服務。");
            Notify();
            return;
        }
        if (!RuntimeHelpEnabled)
        {
            _native.Info("執行求救", "目前 Log 沒有偵測到錯誤。");
            Notify();
            return;
        }
        AgentPrompt = CursorLauncher.BuildRuntimeLogPrompt(
            Catalog!.Root, svc.Label, LogText, StartErrorFor(svc));
        AgentTitle = "執行求救";
        AgentIntro = AgentLaunchIntro();
        Dialog = "agent";
        Notify();
    }

    public void OpenRuntimeHelpFor(string serviceId)
    {
        SelectService(serviceId);
        OpenRuntimeHelp();
    }

    public bool IsCompileHelpFor(BuildState project) =>
        LastBuildFailure is not null
        && Catalog is not null
        && BuildFreshness.SameProject(Catalog.Root, project.Path, LastBuildFailure.Target);

    public async Task ConfirmAgentAsync()
    {
        var prompt = AgentPrompt;
        var status = AgentLaunchStatus(AgentTitle);
        CloseDialog();
        await LaunchAgentAsync(prompt, status).ConfigureAwait(false);
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
            _iconUrlCache.Clear();
            Catalog = ServiceCatalogBuilder.Build(Catalog.Root);
        }
        else
            GithubConfigResolver.SaveLocal(Catalog.Root, GithubDraft);
        var msg = "已儲存 GitHub 設定";
        if (GithubApplyRemote)
            msg = await GithubConfigResolver.ApplyRemoteAsync(Catalog, GithubDraft).ConfigureAwait(false);
        CloseDialog();
        PresentOutcome("GitHub 設定", msg);
    }

    public void SaveDeploy()
    {
        if (Catalog is null)
            return;
        if (DeploySaveTarget == "manifest")
        {
            DeployConfigResolver.WriteManifest(Catalog, DeployDraft);
            _iconUrlCache.Clear();
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
        if (ReleaseRun is { Busy: true })
            return;
        if (Dialog == "release-progress")
        {
            var ret = _releaseReturnDialog;
            _releaseReturnDialog = null;
            ReleaseRun = null;
            InfoCopied = false;
            Dialog = ret == "release" ? "release" : null;
            Notify();
            return;
        }

        var resumeIssue = Dialog is "branch" or "agent" ? _resumeIssueAfterBranch : null;
        _resumeIssueAfterBranch = null;
        CompleteLeaveGate(false);
        DoctorView = null;
        DoctorCopied = false;
        ReleaseList = null;
        InfoReport = null;
        JobResult = null;
        ReleaseRun = null;
        _releaseReturnDialog = null;
        InfoCopied = false;
        if (resumeIssue is not null)
        {
            Dialog = "issue";
            if (ActiveIssue is null || ActiveIssue.Number != resumeIssue.Number)
                OpenIssueDialog(resumeIssue);
            else
                Notify();
            return;
        }
        Dialog = null;
        ClearIssueView();
        Notify();
    }

    public void ForceLeaveGate()
    {
        CompleteLeaveGate(true);
        Notify();
    }

    void CompleteLeaveGate(bool force)
    {
        if (_leaveGateTcs is null && Dialog != "leave-gate")
            return;
        var tcs = _leaveGateTcs;
        _leaveGateTcs = null;
        LeaveGateAction = "";
        LeaveGateReason = null;
        if (Dialog == "leave-gate")
            Dialog = null;
        tcs?.TrySetResult(force);
    }

    public async Task OpenReleaseListAsync()
    {
        if (!RequireCatalog())
            return;
        ReleaseListView? list = null;
        await RunJobAsync("Release 列表…", async () =>
        {
            list = await GitHubService.LoadReleaseListAsync(Catalog!).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (list is null)
            return;
        ReleaseList = list;
        InfoReport = null;
        JobResult = null;
        InfoCopied = false;
        Dialog = "releases";
        Notify();
    }

    public async Task OpenGithubStatusAsync()
    {
        InfoReport? report = null;
        await RunJobAsync("GitHub 狀態…", async () =>
        {
            report = await GitHubService.StatusViewAsync(Catalog).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (report is not null)
            ShowInfoReport(report);
    }

    public void OpenReleaseItem(ReleaseItem item)
    {
        if (Catalog is null)
            return;
        _ = GitHubService.OpenReleasesAsync(Catalog, tag: item.Tag);
    }

    public void OpenReleaseListOnGithub()
    {
        if (Catalog is null || string.IsNullOrEmpty(ReleaseList?.ReleasesUrl))
        {
            _native.Info("無法開啟", "請先完成 GitHub 設定（owner/repo）。");
            return;
        }
        CliUtil.OpenUrl(ReleaseList.ReleasesUrl);
    }

    public async Task InvokeInfoPrimaryAsync()
    {
        switch (InfoReport?.PrimaryAction)
        {
            case "open-github":
                if (Catalog is null || !await GitHubService.OpenOnGithubAsync(Catalog).ConfigureAwait(false))
                    _native.Info("無法開啟", "請先完成 GitHub 設定（owner/repo）。");
                break;
            case "edit-deploy":
                OpenDeployDialog();
                break;
            case "open-deploy":
                if (Catalog is null || !DeployConfigResolver.OpenHost(DeployConfigResolver.Resolve(Catalog)))
                    _native.Info("未設定網址", "請在部署設定中填寫對外網址或主機。");
                break;
        }
    }

    public async Task CopyInfoAsync()
    {
        var text = InfoReport?.Text
            ?? ReleaseList?.ToText()
            ?? ReleaseRun?.CopyText
            ?? JobResult?.Detail
            ?? JobResult?.Summary;
        if (string.IsNullOrWhiteSpace(text) || Js is null)
            return;
        try
        {
            await Js.InvokeVoidAsync("aiConsole.copyText", text).ConfigureAwait(false);
            InfoCopied = true;
            JobText = "已複製到剪貼簿。";
        }
        catch
        {
            InfoCopied = false;
            JobText = "無法複製到剪貼簿。";
        }
        Notify();
    }

    void ShowInfoReport(InfoReport report)
    {
        InfoReport = report;
        ReleaseList = null;
        JobResult = null;
        ReleaseRun = null;
        _releaseReturnDialog = null;
        InfoCopied = false;
        Dialog = "info";
        Notify();
    }

    void ShowJobResult(string title, string tone, string summary, string? detail)
    {
        JobResult = new JobResultView(JobResultView.CleanTitle(title), tone, summary, detail);
        InfoReport = null;
        ReleaseList = null;
        ReleaseRun = null;
        _releaseReturnDialog = null;
        InfoCopied = false;
        Dialog = "job-result";
        Notify();
    }

    void PresentOutcome(string title, string? message, bool error = false)
    {
        if (error)
        {
            JobText = "錯誤";
            ShowJobResult(title, "error", FirstLine(message ?? "發生錯誤"), message);
            return;
        }
        if (JobResultView.IsDense(message))
        {
            ShowJobResult(title, "ok", FirstLine(message!), message);
            return;
        }
        if (!string.IsNullOrWhiteSpace(message))
            JobText = message.Trim();
        Notify();
    }

    /// <summary>
    /// 雲端後端不詢問。本機 IDE 只關這個專案的視窗；CLI 終端機仍問是否關閉該應用。
    /// </summary>
    bool ConfirmCloseLocalAgent()
    {
        if (Catalog is null || !CurrentAgent.CanCloseIde || CurrentAgent.Kind == AgentBackendKind.Cloud)
            return false;
        var body = CurrentAgent.Kind == AgentBackendKind.Ide
            ? $"要一併關閉這個專案的 {AgentDisplayName} 視窗嗎？其他專案的視窗不會關。"
            : $"要一併關閉 {AgentDisplayName} 嗎？";
        return _native.Confirm($"關閉 {AgentDisplayName}", body);
    }

    public async Task ExitAsync()
    {
        if (!await EnsureClearToLeaveAsync("離開").ConfigureAwait(false))
            return;
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

        var closeIde = ConfirmCloseLocalAgent();
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
            var err = CurrentAgent.CloseIde(Catalog?.Root);
            if (err is not null && !_native.Confirm($"關閉 {AgentDisplayName}", $"關閉 {AgentDisplayName} 時發生問題：\n{err}\n\n仍要離開控制台嗎？"))
                return;
        }
        CloseWorkSession();
        _native.Close();
        await Task.CompletedTask;
    }

    public Task RefreshBuildStatesAsync() => RefreshBuildStatesAsync(clearActivity: true);

    public Task RefreshBuildStatesAsync(bool clearActivity)
    {
        var catalog = Catalog;
        if (catalog is null)
            return Task.CompletedTask;
        return Task.Run(() =>
        {
            var previous = clearActivity
                ? []
                : Projects.Where(p => !string.IsNullOrEmpty(p.Activity))
                    .ToDictionary(p => p.Path, p => p.Activity, StringComparer.OrdinalIgnoreCase);
            var proj = BuildFreshness.AllProjectBuildStates(catalog);
            if (previous.Count > 0)
            {
                proj = proj.Select(p => previous.TryGetValue(p.Path, out var act)
                    ? p with { Activity = act }
                    : p).ToList();
            }
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
        CloseWorkSession();
        StopDocsServe();
        _askCts?.Cancel();
        _askCts?.Dispose();
        _askProbeCts?.Cancel();
        _askProbeCts?.Dispose();
        _githubLoginCts?.Cancel();
        _githubLoginCts?.Dispose();
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
        if (GithubManaged && !GithubLoggedIn)
        {
            GithubAuthHint = "此專案由 GitHub 管理。請先登入。";
            Dialog = "gh-login";
            Notify();
            return false;
        }
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
        if (JobBusy)
        {
            _native.Info("忙碌中", $"請等待目前工作完成（{JobText}），再發行 Release。");
            return;
        }
        ReleaseHint = "正在讀取 GitHub 上的發行紀錄…";
        ReleaseTag = "";
        ReleaseTitle = "";
        ReleaseNotes = "";
        ReleaseTarget = GitBrief?.Branch ?? "";
        ReleaseDraft = false;
        ReleasePrerelease = false;
        ReleaseGenerateNotes = true;
        ReleaseMakeLatest = true;
        ReleaseAssets.Clear();
        ReleasePackable = ConsoleReleasePack.LooksPackable(Catalog!.Root);
        ReleaseLatestTag = "";
        Dialog = "release";
        Notify();
        ReleaseInspect? inspect = null;
        await RunJobAsync("讀取 Release…", async () =>
        {
            inspect = await GitHubService.InspectReleaseAsync(Catalog!).ConfigureAwait(false);
            return (string?)null;
        }).ConfigureAwait(false);
        if (inspect is null)
        {
            ReleaseHint = "無法讀取 GitHub Release。可關閉後再從操作台「發行 Release…」重試。";
            Notify();
            return;
        }
        ReleasePackable = inspect.Packable;
        ReleaseLatestTag = inspect.LatestGithubTag;
        if (inspect.Packable && !inspect.LatestHasSetup && !string.IsNullOrEmpty(inspect.LatestGithubTag))
            ReleaseTag = inspect.LatestGithubTag;
        else
            ReleaseTag = inspect.SuggestedTag;
        ReleaseTitle = inspect.Packable ? ReleaseTag + " " + AppInfo.Product : ReleaseTag;
        ReleaseNotes = "";
        ReleaseTarget = inspect.CurrentBranch;
        ReleaseDraft = false;
        ReleasePrerelease = false;
        ReleaseGenerateNotes = true;
        ReleaseMakeLatest = true;
        ReleaseAssets.Clear();
        ReleaseHint = inspect.Summary;
        AttachReleaseDistAssets();
        Dialog = "release";
        Notify();
    }

    void AttachReleaseDistAssets()
    {
        if (Catalog is null || !ReleasePackable)
            return;
        ReleaseAssets.RemoveAll(ConsoleReleasePack.IsConsoleDistAsset);
        foreach (var p in ConsoleReleasePack.FindExisting(Catalog.Root, ReleaseTag).ExistingPaths())
        {
            if (!ReleaseAssets.Contains(p))
                ReleaseAssets.Add(p);
        }
    }

    private async Task PublishCurrentBranchAsync()
    {
        if (!RequireCatalog())
            return;
        if (TestBeforePush)
        {
            var passed = await RunTestsAsync(asGate: true).ConfigureAwait(false);
            if (!passed)
                return;
        }
        await RunJobAsync("發布中…", async () => await GitHubService.PublishBranchAsync(Catalog!)).ConfigureAwait(false);
        await RefreshGitStatusAsync().ConfigureAwait(false);
        BeginWatchCiAfterPush();
        await RefreshPullRequestAsync().ConfigureAwait(false);
    }

    public async Task ScaffoldCiWorkflowAsync(bool alreadyConfirmed = false)
    {
        if (!RequireCatalog())
            return;
        if (!alreadyConfirmed
            && !_native.Confirm(
                "補齊 CI workflow",
                "將在 .github/workflows/ci.yml 寫入建置／測試骨架（既有檔不覆蓋）。請審查後再提交。控制台不會代跑 Actions。確定？"))
            return;
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            var branch = string.IsNullOrEmpty(cfg.DefaultBranch) ? "main" : cfg.DefaultBranch;
            var result = CiWorkflow.Ensure(Catalog!.Root, branch);
            JobText = result.Created ? "已寫入 CI workflow" : "CI workflow 已存在";
            PresentOutcome("CI workflow", result.Message);
        }
        catch (Exception ex)
        {
            PresentOutcome("補齊 CI workflow", ex.Message, error: true);
        }
    }

    public async Task OpenOrCreatePrAsync(int? closesIssue = null)
    {
        if (!RequireCatalog())
            return;
        await RefreshPullRequestAsync().ConfigureAwait(false);
        if (PullRequest is { HasPr: true })
        {
            Dialog = "pr";
            Notify();
            return;
        }
        var block = IssueCompletion.CreatePrBlockReason(
            GitBrief,
            GithubDraft.DefaultBranch,
            hasPr: false,
            PullRequest?.Error);
        if (!string.IsNullOrEmpty(block))
        {
            if (ActiveIssue is not null)
            {
                IssueViewHint = block;
                Notify();
            }
            if (IssueCompletion.NeedsTaskBranch(GitBrief, GithubDraft.DefaultBranch)
                && ActiveIssue is { } issue
                && _native.Confirm(
                    "還不能建立 PR",
                    block + "\n\n要先為此任務建立功能分支嗎？未提交的改動會跟著新分支。"))
            {
                _resumeIssueAfterBranch = issue;
                await OpenBranchDialogAsync(
                    IssueCompletion.SuggestIssueBranchName(issue.Number, issue.Title)).ConfigureAwait(false);
                return;
            }
            _native.Info("還不能建立 PR", block);
            return;
        }
        var confirm = closesIssue is > 0
            ? $"目前分支還沒有 PR。要用提交說明自動建立，並在說明寫入 Closes #{closesIssue} 嗎？\n合併後才會關閉這則 Issue。審查、留言與合併請到 GitHub。"
            : "目前分支還沒有 PR。要用提交說明自動建立嗎？\n審查、留言與合併請到 GitHub。";
        if (!_native.Confirm("建立 PR", confirm))
            return;
        await RunJobAsync("建立 PR…", async () => await GitHubService.CreatePullRequestAsync(Catalog!, closesIssue: closesIssue)).ConfigureAwait(false);
        await RefreshPullRequestAsync().ConfigureAwait(false);
        if (PullRequest is { HasPr: true })
        {
            Dialog = "pr";
            Notify();
        }
    }

    public async Task OpenPrDialogAsync()
    {
        if (!RequireCatalog())
            return;
        await RefreshPullRequestAsync().ConfigureAwait(false);
        Dialog = "pr";
        Notify();
    }

    public void OpenPullRequest()
    {
        if (!GitHubService.OpenWorkflowRun(PullRequest?.Url))
            _native.Info("無法開啟", "沒有可開的 PR。請先建立，或到 GitHub 看倉庫。");
    }

    public void OpenPrCheck(string? url)
    {
        if (!GitHubService.OpenWorkflowRun(url))
            _native.Info("無法開啟", "這個檢查沒有網址。");
    }

    private async Task RefreshPullRequestAsync()
    {
        if (Catalog is null || !GithubLoggedIn || !GithubManaged)
        {
            PullRequest = null;
            return;
        }
        try
        {
            PullRequest = await GitHubService.GetPullRequestStatusAsync(Catalog).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PullRequest = PullRequestStatus.Unavailable(FirstLine(ex.Message));
        }
    }

    public async Task OpenCiDialogAsync()
    {
        if (!RequireCatalog())
            return;
        if (!GithubLoggedIn)
        {
            _native.Info("需要登入", "讀 Actions 狀態需要已登入的 GitHub CLI。");
            return;
        }
        await RefreshActionsAsync().ConfigureAwait(false);
        Dialog = "ci";
        Notify();
    }

    public void OpenLatestCiRun()
    {
        var url = Actions?.Latest?.Url;
        if (!GitHubService.OpenWorkflowRun(url))
            _native.Info("無法開啟", "沒有可開的 Actions 執行。請到 GitHub 操作台看倉庫。");
    }

    public void OpenCiRun(string? url)
    {
        if (!GitHubService.OpenWorkflowRun(url))
            _native.Info("無法開啟", "這個執行沒有網址。");
    }

    private async Task RefreshActionsAsync()
    {
        if (Catalog is null || !GithubLoggedIn || !GithubManaged)
        {
            Actions = null;
            return;
        }
        try
        {
            Actions = await GitHubService.GetActionsSnapshotAsync(Catalog).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Actions = ActionsSnapshot.Unavailable(FirstLine(ex.Message));
        }
    }

    private void BeginWatchCiAfterPush()
    {
        if (Catalog is null || !GithubLoggedIn || !GithubManaged)
            return;
        _ciWatchUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        var gen = ++_ciWatchGen;
        var started = DateTimeOffset.UtcNow;
        var branch = GitBrief?.Branch;
        JobText = "已推送，正在等 CI…";
        Notify();
        _ = WatchCiAfterPushAsync(gen, started, branch);
    }

    private async Task WatchCiAfterPushAsync(int gen, DateTimeOffset started, string? branch)
    {
        try
        {
            for (var i = 0; i < 40 && gen == _ciWatchGen && !_cts.IsCancellationRequested; i++)
            {
                await Task.Delay(i < 8 ? 2500 : 8000, _cts.Token).ConfigureAwait(false);
                if (gen != _ciWatchGen || Catalog is null)
                    return;
                await RefreshActionsAsync().ConfigureAwait(false);
                await RefreshPullRequestAsync().ConfigureAwait(false);
                var watched = Actions is null ? null : ActionsStatus.PreferWatched(Actions, branch, started);
                if (watched is null)
                {
                    JobText = "已推送，尚未看到新的 CI";
                    Notify();
                    continue;
                }
                if (watched.IsInProgress)
                {
                    JobText = $"CI 進行中 · {watched.Name}";
                    Notify();
                    continue;
                }
                if (watched.IsFailure)
                {
                    WarnText = "遠端 CI 失敗。摘要列可開 GitHub 看 log。";
                    JobText = "CI 失敗";
                    Notify();
                    return;
                }
                if (watched.IsSuccess)
                {
                    if (WarnText.Contains("CI", StringComparison.Ordinal))
                        WarnText = "";
                    JobText = "CI 通過";
                    Notify();
                    return;
                }
                JobText = watched.ChipText();
                Notify();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch
        {
            // 背景監看失敗不打擾
        }
    }

    private async Task<bool> RunTestsAsync(bool asGate)
    {
        if (!RequireCatalog())
            return false;
        var catalog = Catalog!;
        var targets = TestRunner.TargetsFor(catalog);
        if (targets.Count == 0)
        {
            if (!asGate)
                _native.Info("沒有測試", "這個工作區沒有方案或測試專案可跑。控制台只做一次完整測試，不是 IDE 測試總管。");
            return true;
        }
        if (!await EnsureBuildToolsAsync(targets).ConfigureAwait(false))
            return false;

        LeftTab = "prj";
        BuildText = "";
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        var passed = true;
        Notify();
        await RunJobAsync(asGate ? "發布前測試…" : "測試中…", async () =>
        {
            await BeginBuildBatchAsync(targets).ConfigureAwait(false);
            var allLines = new List<string>();
            string? failedTarget = null;
            var failedCode = 0;
            foreach (var target in targets)
            {
                MarkBuildActivity(target, "building");
                BuildCurrentName = Path.GetFileName(target);
                JobText = $"測試中 {BuildProgressText} · {BuildCurrentName}";
                Notify();
                var header = $"=== test {target} ===";
                allLines.Add(header);
                AppendBuild(header);
                var progress = new Progress<string>(line =>
                {
                    allLines.Add(line);
                    AppendBuild(line);
                });
                var (code, _) = await TestRunner.TestAsync(catalog.Root, target, progress).ConfigureAwait(false);
                var footer = $"exit {code}  ({target})";
                allLines.Add(footer);
                AppendBuild(footer);
                FinishOneBuild(target, code);
                if (code != 0 && failedTarget is null)
                {
                    failedTarget = target;
                    failedCode = code;
                    passed = false;
                }
            }
            if (failedTarget is not null)
                RememberBuildFailure(failedTarget, failedCode, string.Join('\n', allLines));
            JobText = passed ? "測試通過" : "測試失敗";
            return (string?)null;
        }, refreshBuilds: false).ConfigureAwait(false);
        await RefreshBuildStatesAsync(clearActivity: false).ConfigureAwait(false);
        if (JobText == "錯誤")
            passed = false;
        if (!passed && asGate)
            _native.Warn("發布前檢查未過", "本機測試失敗。請看右側「建置／測試」輸出，修正後再發布。");
        return passed;
    }

    private async Task RunBuildActionAsync(string handler)
    {
        if (!RequireCatalog())
            return;
        var catalog = Catalog!;
        LeftTab = "prj";
        BuildText = "";
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Notify();
        var targets = BuildRunner.TargetsFor(catalog, handler);
        if (targets.Count == 0)
        {
            _native.Info("沒有需要編譯的項目", "這個工作區目前沒有可編譯的專案。");
            return;
        }
        if (!await EnsureBuildToolsAsync(targets).ConfigureAwait(false))
            return;
        await RunJobAsync("建置中…", async () =>
        {
            await BeginBuildBatchAsync(targets).ConfigureAwait(false);
            var allLines = new List<string>();
            string? failedTarget = null;
            var failedCode = 0;
            foreach (var target in targets)
            {
                MarkBuildActivity(target, "building");
                BuildCurrentName = Path.GetFileName(BuildFreshness.ToProjectDir(catalog.Root, target));
                JobText = $"建置中 {BuildProgressText} · {BuildCurrentName}";
                Notify();
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
                FinishOneBuild(target, code);
                if (code != 0 && failedTarget is null)
                {
                    failedTarget = target;
                    failedCode = code;
                }
            }
            if (failedTarget is not null)
                RememberBuildFailure(failedTarget, failedCode, string.Join('\n', allLines));
            JobText = BuildProgressText;
            return (string?)null;
        }, refreshBuilds: false).ConfigureAwait(false);
        await RefreshBuildStatesAsync(clearActivity: false).ConfigureAwait(false);
    }

    private async Task LaunchAgentAsync(string prompt, string status)
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
        var backend = CurrentAgent;
        var err = await backend.LaunchAgent(Catalog.Root, prompt, AgentBackendRegistry.CliOverrideFor(backend)).ConfigureAwait(false);
        if (err is not null)
        {
            _native.Warn(backend.DisplayName, err);
            return;
        }
        JobText = status;
        Notify();
    }

    private string AgentLaunchIntro() =>
        CurrentAgent.Id switch
        {
            "cursor" => "確認後會開啟 Cursor 並跳出確認視窗；再按確認即建立 New Agent。錯誤內容會直接帶入提示，不會先寫求助檔。",
            "claude" => "確認後會以 Claude Code deeplink 開啟工作區並帶入提示。完整內容已複製到剪貼簿。",
            "aider" or "codex" => $"確認後會開啟終端機執行 {AgentDisplayName}。完整提示已複製到剪貼簿。",
            "custom" => "確認後會執行你在設定裡填的自訂命令。提示會寫入 .ai_project/agent-prompts/。",
            _ => $"確認後會開啟 {AgentDisplayName}，並把提示複製到剪貼簿（此後端沒有穩定的 Agent deeplink，請在 IDE 內貼上）。",
        };

    private string AgentLaunchStatus(string title) =>
        CurrentAgent.Id switch
        {
            "cursor" => $"{title}已送出，請在 Cursor 跳出視窗按確認",
            "claude" => $"{title}已送出，請在 Claude Code 檢視提示後送出",
            _ => $"{title}已送出（提示已複製，後端：{AgentDisplayName}）",
        };

    private void RefreshAgentDetect()
    {
        var backend = CurrentAgent;
        var detect = backend.Detect(DraftCliOverride(backend));
        AgentAvailable = detect.Available;
        AgentDetectSummary = detect.Summary;
    }

    private string? DraftCliOverride(IAgentBackend backend)
    {
        if (backend.Id == "custom")
        {
            if (string.IsNullOrWhiteSpace(CustomAgentCommand))
                return "";
            return string.IsNullOrWhiteSpace(CustomAgentArgs)
                ? CustomAgentCommand.Trim()
                : CustomAgentCommand.Trim() + " " + CustomAgentArgs.Trim();
        }
        return string.IsNullOrWhiteSpace(AgentCliPath) ? null : AgentCliPath.Trim();
    }

    private async Task BeginBuildBatchAsync(IReadOnlyList<string> targets)
    {
        BuildDone = 0;
        BuildFailedCount = 0;
        BuildTotal = targets.Count;
        BuildCurrentName = "";
        await RefreshBuildStatesAsync(clearActivity: true).ConfigureAwait(false);
        foreach (var target in targets)
            MarkBuildActivity(target, "queued");
        Notify();
    }

    private void FinishOneBuild(string target, int exitCode)
    {
        BuildDone++;
        if (exitCode != 0)
            BuildFailedCount++;
        MarkBuildActivity(target, exitCode == 0 ? "ok" : "failed");
        RefreshOneProject(target, exitCode == 0 ? "ok" : "failed");
        if (Runtime is not null)
        {
            try { BuildReportStore.Write(Runtime, target, exitCode, BuildFreshness.DefaultConfiguration); }
            catch { /* report is optional */ }
        }
        Notify();
    }

    private void ResetBuildProgress()
    {
        BuildDone = 0;
        BuildTotal = 0;
        BuildFailedCount = 0;
        BuildCurrentName = "";
    }

    private void MarkBuildActivity(string target, string activity)
    {
        if (Catalog is null)
            return;
        Projects = Projects
            .Select(p => BuildFreshness.SameProject(Catalog.Root, p.Path, target)
                ? p with { Activity = activity }
                : p)
            .ToList();
    }

    private void RefreshOneProject(string target, string activity)
    {
        if (Catalog is null)
            return;
        var info = Catalog.Projects.FirstOrDefault(p => BuildFreshness.SameProject(Catalog.Root, p.RelDir, target));
        if (info is null)
            return;
        var fresh = BuildFreshness.ProjectBuildState(Catalog.Root, info) with { Activity = activity };
        Projects = Projects
            .Select(p => BuildFreshness.SameProject(Catalog.Root, p.Path, target) ? fresh : p)
            .ToList();
    }

    private void RememberBuildFailure(string target, int exitCode, string log)
    {
        LastBuildFailure = new BuildFailure(target, exitCode, log);
        CompileHelpEnabled = true;
        AppendBuild("建置失敗 — 可點「編譯求救」交給目前 Agent 後端");
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

    public string? StartErrorFor(ServiceEntry svc)
    {
        if (StartErrors.TryGetValue(svc.Id, out var err) && !string.IsNullOrWhiteSpace(err))
            return err;
        if (Catalog is null)
            return null;
        var host = ServiceCatalogBuilder.HostService(Catalog, svc);
        if (host.Id != svc.Id && StartErrors.TryGetValue(host.Id, out err) && !string.IsNullOrWhiteSpace(err))
            return err;
        return null;
    }

    public string? ServiceActivityText(ServiceEntry svc)
    {
        if (ServiceActivities.TryGetValue(svc.Id, out var activity))
            return ServiceActivityMap.Label(activity);
        if (Catalog is null)
            return null;
        var host = ServiceCatalogBuilder.HostService(Catalog, svc);
        if (host.Id != svc.Id && ServiceActivities.TryGetValue(host.Id, out activity))
            return ServiceActivityMap.Label(activity);
        return null;
    }

    private void MarkServiceActivity(IEnumerable<ServiceEntry> targets, string activity, bool withHosted = true)
    {
        if (JobBusy || Catalog is null)
            return;
        var ids = withHosted
            ? ServiceActivityMap.IdsWithHosted(Catalog.Services, targets)
            : targets.Select(t => t.Id);
        ServiceActivityMap.Set(ServiceActivities, ids, activity);
        Notify();
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

    private void LoadAudit(bool reloadPolicy)
    {
        if (Runtime is null || Catalog is null)
        {
            AuditEntries = [];
            AuditTotal = 0;
            if (reloadPolicy)
                AuditPolicyText = "";
            return;
        }

        try
        {
            var (total, entries) = McpAuditLog.ReadRecent(Runtime, 80);
            AuditTotal = total;
            AuditEntries = entries;
        }
        catch
        {
            AuditEntries = [];
            AuditTotal = 0;
        }

        if (reloadPolicy)
        {
            try { AuditPolicyText = McpPolicy.Load(Catalog.Root).DoctorLine(); }
            catch { AuditPolicyText = ""; }
        }
    }

    private bool RequireCatalog()
    {
        if (Catalog is not null && Runtime is not null)
            return true;
        _native.Info("尚未選擇專案", "請先選擇專案目錄。");
        return false;
    }

    private HashSet<string> GroupRunnableIds(string key)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var node = ServiceGroupTree.Find(ServiceGroupRoots, key);
        if (node is null)
            return ids;
        foreach (var svc in node.Descendants())
        {
            if (IsSelfService(svc) || !string.IsNullOrEmpty(svc.HostedBy))
                continue;
            ids.Add(svc.Id);
        }
        return ids;
    }

    private async Task<bool> EnsureStartToolsAsync(ServiceEntry? svc = null) =>
        await EnsureStartToolsAsync(svc is null
            ? ServiceCatalogBuilder.OrderedRunnable(Catalog!)
            : [ServiceCatalogBuilder.HostService(Catalog!, svc)]).ConfigureAwait(false);

    private async Task<bool> EnsureStartToolsAsync(IEnumerable<ServiceEntry> targets)
    {
        var catalog = Catalog!;
        var toolIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in targets)
        {
            var path = ProcessSupervisor.ProjectPathFor(catalog, item);
            var stackId = TechStackDetector.StackIdForPath(path);
            if (string.IsNullOrEmpty(stackId) && path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                stackId = "python";
            if (string.IsNullOrEmpty(stackId))
                stackId = "dotnet";
            foreach (var id in TechStackCatalog.RequiredToolIdsFor([stackId]))
                toolIds.Add(id);
        }
        var dir = catalog.Root;
        var missing = ToolchainBootstrap.MissingTools(toolIds, dir);
        if (missing.Count == 0)
            return true;
        var names = string.Join("、", missing.Select(m => m.DisplayName));
        if (!_native.Confirm("缺少開發環境", $"找不到 {names}。要現在安裝嗎？\n\n安裝完成後請再按一次啟動。"))
            return false;
        foreach (var spec in missing)
            await InstallToolchainAsync(spec.Id, confirm: false).ConfigureAwait(false);
        ToolchainBootstrap.RefreshProcessPath();
        missing = ToolchainBootstrap.MissingTools(toolIds, dir);
        if (missing.Count > 0)
        {
            _native.Error("缺少工具", "仍缺少：" + string.Join("、", missing.Select(m => m.DisplayName)) + "。請看環境體檢。");
            return false;
        }
        return true;
    }

    private async Task<bool> EnsureBuildToolsAsync(IEnumerable<string> targets)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            var stackId = StackCommands.StackIdFor(target);
            if (string.IsNullOrEmpty(stackId))
                stackId = "dotnet";
            foreach (var id in TechStackCatalog.RequiredToolIdsFor([stackId]))
                ids.Add(id);
        }
        var missing = ToolchainBootstrap.MissingTools(ids, Catalog?.Root);
        if (missing.Count == 0)
            return true;
        var names = string.Join("、", missing.Select(m => m.DisplayName));
        if (!_native.Confirm("缺少開發環境", $"編譯／測試需要 {names}。要現在安裝嗎？"))
            return false;
        foreach (var spec in missing)
            await InstallToolchainAsync(spec.Id, confirm: false).ConfigureAwait(false);
        ToolchainBootstrap.RefreshProcessPath();
        missing = ToolchainBootstrap.MissingTools(ids, Catalog?.Root);
        if (missing.Count > 0)
        {
            _native.Error("缺少工具", "仍缺少：" + string.Join("、", missing.Select(m => m.DisplayName)) + "。請看環境體檢。");
            return false;
        }
        return true;
    }

    private async Task<bool> RunReleaseProgressAsync(
        ReleaseRunState run,
        string? returnDialog,
        Func<IProgress<string>, Task<string?>> fn)
    {
        if (JobBusy)
        {
            _native.Info("忙碌中", "請等待目前工作完成。");
            return false;
        }
        ReleaseRun = run;
        _releaseReturnDialog = returnDialog;
        JobResult = null;
        InfoReport = null;
        ReleaseList = null;
        InfoCopied = false;
        Dialog = "release-progress";
        JobBusy = true;
        run.Begin();
        JobText = run.Title + "…";
        Notify();
        var progress = new Progress<string>(text =>
        {
            run.Apply(text);
            if (!string.IsNullOrWhiteSpace(run.StatusText))
                JobText = run.StatusText;
            Notify();
        });
        string? err = null;
        string? msg = null;
        try
        {
            msg = await Task.Run(() => fn(progress)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            err = ex.Message;
        }
        JobBusy = false;
        if (err is not null)
        {
            run.Fail(err);
            JobText = "錯誤";
        }
        else
        {
            run.Succeed(msg);
            JobText = string.IsNullOrWhiteSpace(run.Headline) ? "完成" : run.Headline;
        }
        Notify();
        UpdateReady();
        await RefreshBuildStatesAsync().ConfigureAwait(false);
        return err is null;
    }

    private async Task RunJobAsync(string title, Func<Task<string?>> fn, bool refreshBuilds = true)
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
        if (err is not null)
            JobText = "錯誤";
        else if (!refreshBuilds && BuildTotal > 0 && !string.IsNullOrEmpty(BuildProgressText))
            JobText = BuildProgressText;
        else if (!string.IsNullOrWhiteSpace(msg) && !JobResultView.IsDense(msg))
            JobText = msg.Trim();
        else
            JobText = "完成";
        Notify();
        if (err is not null)
            PresentOutcome(title, err, error: true);
        else if (JobResultView.IsDense(msg))
            PresentOutcome(title, msg);
        UpdateReady();
        if (refreshBuilds)
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
                if (ServiceActivityMap.ClearOpening(ServiceActivities) > 0)
                    Notify();
                healthEvery++;
                if (healthEvery % 3 == 0 && Catalog is not null)
                {
                    var catalog = Catalog;
                    var health = new Dictionary<string, bool>();
                    foreach (var svc in catalog.Services)
                        health[svc.Id] = await ProcessSupervisor.ProbeHealthAsync(catalog, svc).ConfigureAwait(false);
                    Health.Clear();
                    foreach (var kv in health)
                        Health[kv.Key] = kv.Value;
                    StartErrorMap.ClearHealthy(StartErrors, health);
                    var dead = Runtime is null ? null : ProcessSupervisor.DeadStartedIds(catalog, Runtime);
                    ServiceActivityMap.Reconcile(ServiceActivities, health, StartErrors, dead);
                    UpdateReady();
                }
                var gitEvery = _autoSyncSkippedDirty ? 2 : 8;
                if (healthEvery % gitEvery == 0 && Catalog is not null)
                    await RefreshGitStatusAsync().ConfigureAwait(false);
                if (healthEvery % 5 == 0 && Catalog is not null)
                    LoadAudit(reloadPolicy: false);
                if (healthEvery % 40 == 0 && Catalog is not null && GithubLoggedIn && GithubManaged)
                    await RefreshIssuesAsync().ConfigureAwait(false);
                var watchingCi = WatchingCi;
                if (watchingCi && healthEvery % 6 == 0 && Catalog is not null && GithubLoggedIn && GithubManaged)
                {
                    await RefreshActionsAsync().ConfigureAwait(false);
                    await RefreshPullRequestAsync().ConfigureAwait(false);
                }
                else if (!watchingCi && healthEvery % 45 == 0 && Catalog is not null && GithubLoggedIn && GithubManaged)
                {
                    await RefreshActionsAsync().ConfigureAwait(false);
                    await RefreshPullRequestAsync().ConfigureAwait(false);
                }
                if (healthEvery % 37 == 0)
                    _workHours.Touch();
                Notify();
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private async Task StampWorkHoursProjectAsync()
    {
        if (Catalog is null)
            return;
        try
        {
            var cfg = await GithubConfigResolver.ResolveAsync(Catalog).ConfigureAwait(false);
            var slug = cfg.LooksGithubHosted() ? cfg.Slug() : "";
            if (!string.IsNullOrEmpty(slug))
                _workHours.BindGithubSlug(Catalog.Root, slug);
        }
        catch
        {
            // 沒有遠端不擋計時
        }
    }

    private void ResetToStartup()
    {
        _workHours.End();
        _iconUrlCache.Clear();
        Catalog = null;
        Runtime = null;
        SelectedServiceId = null;
        LastBuildFailure = null;
        CompileHelpEnabled = false;
        Health.Clear();
        StartErrors.Clear();
        ServiceActivities.Clear();
        _collapsedServiceGroups.Clear();
        _collapsedProjectGroups.Clear();
        Projects = [];
        WarnText = "";
        GitStatusText = "";
        GitBrief = null;
        GithubHubOpen = false;
        _autoSyncSkippedDirty = false;
        Actions = null;
        PullRequest = null;
        _ciWatchUntil = null;
        _ciWatchGen++;
        BranchList = [];
        NewBranchName = "";
        BranchDialogHint = "";
        _resumeIssueAfterBranch = null;
        CommitMessage = "";
        CommitSubject = "";
        CommitBody = "";
        CommitHint = "";
        CommitSuggestHint = "";
        CommitPushAfter = false;
        CommitChanges = [];
        CommitSelected.Clear();
        CommitFileQuery = "";
        CommitPreviewPath = null;
        CommitPreviewDiff = "";
        LogFilter = "";
        LogTitle = "Log · （未選服務）";
        LogText = "";
        BuildText = "";
        AuditEntries = [];
        AuditTotal = 0;
        AuditPolicyText = "";
        AuditFilter = "";
        CancelAsk();
        AskDraft = "";
        AskBusy = false;
        AskStatus = "";
        AskMessages = [];
        ResetBuildProgress();
        ReadyText = "就緒 0 / 0";
        JobText = "待命";
        JobBusy = false;
        _logOffset = 0;
        LeftTab = "svc";
        AskPanelOpen = false;
        AuditPanelOpen = false;
        CompleteLeaveGate(false);
        Dialog = null;
        DoctorView = null;
        DoctorCopied = false;
        ReleaseList = null;
        InfoReport = null;
        JobResult = null;
        InfoCopied = false;
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
        AgentTitle = "編譯求救";
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
        ReleasePackable = false;
        ReleaseAssets.Clear();
        _pendingOpenCursor = false;
        GithubAuthBusy = false;
        GithubAuthHint = "";
        GithubLoginCode = "";
        GithubLoginUrl = "";
        GithubLoginBrowserOpened = false;
        GithubLoginCodeCopied = false;
        IntakeDoc = new();
        SelectedIntakeId = null;
        IntakeHint = "";
        IntakeBusy = false;
        Collaborators = [];
        GovernanceIssues = [];
        HoursInbox = [];
        HoursInboxHint = "";
        ReviewerDraft = "";
        ClearIssueLists();
        ClearDocsState();
    }

    private async Task RestoreLastProjectOnStartAsync()
    {
        if (!RestoreLastProject || !ConsoleProcess.IsPrimary)
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

    private async Task RefreshGitStatusThenNotifyAsync()
    {
        await RefreshGitStatusAsync().ConfigureAwait(false);
        Notify();
    }

    private async Task RefreshGitStatusAsync()
    {
        var root = Catalog?.Root;
        if (string.IsNullOrEmpty(root))
        {
            GitBrief = null;
            GitStatusText = "";
            ReconcileAutoSyncSkipMessage();
            return;
        }
        try
        {
            var brief = await GitHubService.TryBriefStatusAsync(root).ConfigureAwait(false);
            GitBrief = brief;
            GitStatusText = brief?.Format() ?? "";
            ReconcileAutoSyncSkipMessage();
        }
        catch
        {
            GitBrief = null;
            GitStatusText = "";
        }
    }

    private void ReconcileAutoSyncSkipMessage()
    {
        if (!_autoSyncSkippedDirty && JobText != AutoSyncSkippedDirtyText)
            return;
        if (GitBrief is { DirtyCount: > 0 })
            return;
        _autoSyncSkippedDirty = false;
        if (!JobBusy && JobText == AutoSyncSkippedDirtyText)
            JobText = "工作區已乾淨";
    }

    private void ApplyStartResults(IReadOnlyList<(string Id, string Label, string? Error)> results)
    {
        if (results.Count == 0)
            return;
        foreach (var (id, _, error) in results)
            StartErrorMap.Apply(StartErrors, id, error);
        ServiceActivityMap.ClearFailed(ServiceActivities, StartErrors);
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

    private void Notify()
    {
        SyncWindowTitle();
        Changed?.Invoke();
    }

    private void SyncWindowTitle()
    {
        var name = Catalog?.Name;
        var title = string.IsNullOrWhiteSpace(name)
            ? $"{AppInfo.Product} v{AppInfo.Version}"
            : $"{name} · {AppInfo.Product} v{AppInfo.Version}";
        _native.SetTitle(title);
    }
}
