using System.Text.Json;
using AiProject.Console.Core.Build;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Stack;

/// <summary>
/// 堆疊操作面：桌面控制台與 MCP 共用。Agent 透過這些方法代替開發管理者點按鈕。
/// </summary>
public sealed class StackWorkspace
{
    public ProjectCatalog Catalog { get; }
    public ProjectRuntime Runtime { get; }
    public string Root => Catalog.Root;

    public StackWorkspace(ProjectCatalog catalog, ProjectRuntime runtime)
    {
        Catalog = catalog;
        Runtime = runtime;
        Runtime.Ensure();
    }

    public static StackWorkspace Open(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DirectoryNotFoundException("專案目錄不存在：" + root);
        var catalog = ServiceCatalogBuilder.Build(Path.GetFullPath(root));
        return new StackWorkspace(catalog, new ProjectRuntime(catalog.Root));
    }

    public async Task<string> StackStatusAsync()
    {
        var health = await ProbeHealthAsync().ConfigureAwait(false);
        var svcStates = BuildFreshness.AllServiceBuildStates(Catalog);
        var prj = BuildFreshness.AllProjectBuildStates(Catalog);
        var ready = Catalog.Services.Count(s => health.GetValueOrDefault(s.Id));
        var staleSvc = svcStates.Count(s => s.Status is "stale" or "unbuilt");
        var stalePrj = prj.Count(p => p.Status is "stale" or "unbuilt");
        var rows = Catalog.Services.Select(s => new
        {
            s.Id,
            s.Label,
            s.Port,
            online = health.GetValueOrDefault(s.Id),
            hostedBy = s.HostedBy,
            build = svcStates.FirstOrDefault(b => b.Id == s.Id)?.Status,
        });
        return Json(new
        {
            root = Root,
            name = Catalog.Name,
            ready = $"{ready}/{Catalog.Services.Count}",
            staleServices = staleSvc,
            staleProjects = stalePrj,
            services = rows,
        });
    }

    public async Task<string> DutySummaryAsync()
    {
        var health = await ProbeHealthAsync().ConfigureAwait(false);
        var ready = Catalog.Services.Count(s => health.GetValueOrDefault(s.Id));
        var offline = Catalog.Services.Count - ready;
        var staleSvc = BuildFreshness.AllServiceBuildStates(Catalog).Count(s => s.Status is "stale" or "unbuilt");
        var stalePrj = BuildFreshness.AllProjectBuildStates(Catalog).Count(p => p.Status is "stale" or "unbuilt");
        var (_, audit) = McpAuditLog.ReadRecent(Runtime, 80);
        var fails = audit.Where(e => !e.Ok).ToList();
        var incidents = fails.Where(e => e.IsIncident).ToList();
        var last = fails.Count > 0 ? fails[^1] : null;
        var lastIncident = incidents.Count > 0 ? incidents[^1] : null;
        var attention = DutySummary.Attention(offline, stalePrj, incidents.Count, lastIncident?.Tool);
        return Json(new
        {
            root = Root,
            name = Catalog.Name,
            ready = $"{ready}/{Catalog.Services.Count}",
            offline,
            staleServices = staleSvc,
            staleProjects = stalePrj,
            auditFails = fails.Count,
            auditIncidents = incidents.Count,
            lastAuditFail = last is null
                ? null
                : new { tool = last.Tool, error = last.Error, utc = last.Utc.ToString("o") },
            attention,
            ok = DutySummary.IsClear(offline, stalePrj, incidents.Count),
        });
    }

    public string ListServices() =>
        Json(Catalog.Services.Select(s => new
        {
            s.Id,
            s.Label,
            s.Port,
            s.Health,
            s.OpenUrl,
            s.Group,
            s.HostedBy,
            s.Project,
        }));

    public string ListProjects()
    {
        var states = BuildFreshness.AllProjectBuildStates(Catalog);
        var needsRebuild = states.Count(p => p.Status is "stale" or "unbuilt");
        return Json(new
        {
            needsRebuild,
            summary = needsRebuild == 0
                ? "無需重編，全部最新"
                : $"需重編 {needsRebuild}",
            projects = states.Select(p => new
            {
                p.Name,
                p.Path,
                p.Kind,
                p.Status,
                badge = BuildFreshness.BadgeText(p),
                needsRebuild = p.Status is "stale" or "unbuilt",
                p.Reason,
                lastBuild = BuildFreshness.FormatAgo(p.LastBuildUtc),
                newestSource = string.IsNullOrEmpty(p.NewestSourcePath) ? "" : Path.GetFileName(p.NewestSourcePath),
            }),
        });
    }

    public string BuildFreshnessReport() => ListProjects();

    public async Task<string> BuildAsync(string mode, string? path = null)
    {
        mode = (mode ?? "stale").Trim().ToLowerInvariant();
        IReadOnlyList<string> targets;
        if (mode is "one" or "project")
        {
            if (string.IsNullOrWhiteSpace(path))
                return Error("build one 需要 path（相對專案根或絕對路徑）");
            targets = [Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)))];
        }
        else
        {
            var handler = mode switch
            {
                "services" => "build_services",
                "projects" or "all" => "build_projects",
                _ => "build_stale",
            };
            targets = BuildRunner.TargetsFor(Catalog, handler);
        }

        if (targets.Count == 0)
            return Json(new { ok = true, message = "沒有需要編譯的項目", done = 0, failed = 0 });

        var failed = new List<object>();
        var done = 0;
        foreach (var target in targets)
        {
            var (code, log) = await BuildRunner.BuildAsync(Root, target, progress: null).ConfigureAwait(false);
            done++;
            try { BuildReportStore.Write(Runtime, target, code, BuildFreshness.DefaultConfiguration); }
            catch { /* optional */ }
            if (code != 0)
            {
                var tail = log.Length > 2000 ? log[^2000..] : log;
                failed.Add(new { target, exitCode = code, logTail = tail });
            }
        }
        return Json(new
        {
            ok = failed.Count == 0,
            done,
            total = targets.Count,
            failed = failed.Count,
            failures = failed,
        });
    }

    public string StartService(string id)
    {
        var svc = RequireService(id);
        if (ServiceCatalogBuilder.IsCurrentConsole(Catalog, svc))
            return Error(ProcessSupervisor.SelfConsoleStartMessage);
        if (!string.IsNullOrEmpty(svc.HostedBy))
            return Error($"「{svc.Label}」隨 {svc.HostedBy} 啟動，請啟動宿主。");
        var err = ProcessSupervisor.TryStartService(Catalog, Runtime, svc);
        return err is null
            ? Json(new { ok = true, id = svc.Id, label = svc.Label })
            : Error(err);
    }

    public string StopService(string id)
    {
        var svc = RequireService(id);
        if (ServiceCatalogBuilder.IsCurrentConsole(Catalog, svc))
            return Error("這是目前這個控制台，停止請用右上角「離開」。");
        ProcessSupervisor.StopService(Catalog, Runtime, svc);
        return Json(new { ok = true, id = svc.Id, label = svc.Label, stopped = true });
    }

    public async Task<string> StartAllAsync()
    {
        var health = await ProbeHealthAsync().ConfigureAwait(false);
        var results = ProcessSupervisor.StartOffline(Catalog, Runtime, health);
        return Json(new
        {
            ok = results.All(r => r.Error is null),
            started = results.Where(r => r.Error is null).Select(r => r.Id),
            failed = results.Where(r => r.Error is not null).Select(r => new { r.Id, r.Label, error = r.Error }),
        });
    }

    public string StopAll()
    {
        ProcessSupervisor.StopAll(Catalog, Runtime);
        return Json(new { ok = true, stopped = true });
    }

    public string GetLog(string id, int tail = 80)
    {
        var svc = RequireService(id);
        var host = ServiceCatalogBuilder.HostService(Catalog, svc);
        var path = Runtime.LogPath(host.Stem);
        if (!File.Exists(path))
            return Json(new { id = svc.Id, path, lines = Array.Empty<string>(), message = "尚無 Log" });
        var lines = File.ReadAllLines(path);
        if (tail < 1)
            tail = 80;
        var slice = lines.Length > tail ? lines[^tail..] : lines;
        return Json(new { id = svc.Id, path, lineCount = lines.Length, lines = slice });
    }

    public string Doctor() => ProcessSupervisor.DoctorReport(Catalog);

    public string DocsStatus()
    {
        var status = DocsService.Scan(Root);
        return Json(new
        {
            ok = true,
            health = status.Health.ToString().ToLowerInvariant(),
            label = status.Label(),
            fileCount = status.FileCount,
            stubCount = status.StubCount,
            hasToc = status.HasToc,
            hasDocfx = status.HasDocfx,
            hasWorkflow = status.HasWorkflow,
            missingScaffold = status.MissingScaffold,
            docsRoot = status.DocsRoot,
        });
    }

    public string ListDocs()
    {
        var status = DocsService.Scan(Root);
        return Json(new
        {
            ok = true,
            count = status.Files.Count,
            files = status.Files.Select(f => new
            {
                path = f.RelPath,
                title = f.Title,
                stub = f.IsStub,
                config = f.IsConfig,
            }),
        });
    }

    public string ReadDoc(string path)
    {
        if (!DocsService.IsSafeRelPath(path))
            return Error("path 必須是 docs/ 內的相對路徑，例如 user/getting-started.md。");
        var text = DocsService.Read(Root, path);
        return Json(new { ok = true, path = path.Replace('\\', '/'), text });
    }

    public async Task<string> CiStatusAsync()
    {
        var local = CiWorkflow.Describe(Root);
        try
        {
            var snap = await GitHubService.GetActionsSnapshotAsync(Catalog).ConfigureAwait(false);
            return Json(new
            {
                ok = string.IsNullOrEmpty(snap.Error),
                chip = snap.ChipText(),
                tone = snap.ChipTone(),
                hasWorkflows = snap.HasWorkflows,
                localWorkflows = CiWorkflow.ListFiles(Root),
                localKind = local.Badge,
                error = snap.Error,
                latest = snap.Latest is null
                    ? null
                    : new
                    {
                        snap.Latest.Name,
                        title = snap.Latest.DisplayTitle,
                        snap.Latest.Status,
                        snap.Latest.Conclusion,
                        branch = snap.Latest.HeadBranch,
                        snap.Latest.Url,
                    },
                runs = snap.Runs.Select(r => new
                {
                    r.Name,
                    title = r.DisplayTitle,
                    r.Status,
                    r.Conclusion,
                    branch = r.HeadBranch,
                    r.Url,
                }),
            });
        }
        catch (Exception ex)
        {
            return Json(new
            {
                ok = false,
                chip = "CI 無法讀取",
                hasWorkflows = CiWorkflow.HasAny(Root),
                localWorkflows = CiWorkflow.ListFiles(Root),
                error = ex.Message,
            });
        }
    }

    public async Task<string> PrStatusAsync()
    {
        try
        {
            var snap = await GitHubService.GetPullRequestStatusAsync(Catalog).ConfigureAwait(false);
            return Json(new
            {
                ok = string.IsNullOrEmpty(snap.Error),
                hasPr = snap.HasPr,
                chip = snap.ChipText(),
                tone = snap.ChipTone(),
                readyForReview = snap.ReadyForReview,
                snap.Title,
                snap.Url,
                snap.IsDraft,
                head = snap.Head,
                @base = snap.Base,
                passed = snap.PassedCount,
                failed = snap.FailedCount,
                pending = snap.PendingCount,
                hint = snap.Hint(),
                error = snap.Error,
                checks = snap.Checks.Select(c => new
                {
                    c.Name,
                    result = c.ResultText(),
                    c.Url,
                }),
            });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, hasPr = false, chip = "PR 無法讀取", error = ex.Message });
        }
    }

    public async Task<string> GitStatusAsync()
    {
        var brief = await GitHubService.TryBriefStatusAsync(Root).ConfigureAwait(false);
        return Json(new
        {
            root = Root,
            status = brief?.Format() ?? "不是 git 倉或無法讀取",
            branch = brief?.Branch,
            dirty = brief?.DirtyCount,
            ahead = brief?.Ahead,
            behind = brief?.Behind,
            hasUpstream = brief?.HasUpstream,
            leaveBlock = brief?.LeaveBlockReason(),
        });
    }

    public async Task<Dictionary<string, bool>> ProbeHealthAsync()
    {
        var health = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in Catalog.Services)
            health[svc.Id] = await ProcessSupervisor.ProbeHealthAsync(Catalog, svc).ConfigureAwait(false);
        return health;
    }

    ServiceEntry RequireService(string id)
    {
        var svc = ServiceCatalogBuilder.ById(Catalog, (id ?? "").Trim());
        if (svc is null)
            throw new ArgumentException("找不到服務：" + id + "。可用 list_services 查看 id。");
        return svc;
    }

    static string Json(object value) => JsonSerializer.Serialize(value, JsonUtil.Options);

    static string Error(string message) => Json(new { ok = false, error = message });
}
