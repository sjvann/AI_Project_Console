using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Stack;
using AiProject.Console.Core.Update;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.ProcessOps;

public enum DoctorLevel
{
    Ok,
    Warn,
    Missing,
    Info,
}

public sealed record DoctorItem(
    string Label,
    string Value,
    DoctorLevel Level,
    string? Detail = null,
    string? Badge = null,
    string? ActionId = null,
    string? ActionLabel = null,
    string? HowTo = null)
{
    public string StatusText =>
        Badge ?? Level switch
        {
            DoctorLevel.Ok => "正常",
            DoctorLevel.Warn => "需注意",
            DoctorLevel.Missing => "缺少",
            _ => "",
        };
}

public sealed record DoctorSection(
    string Id,
    string Title,
    IReadOnlyList<DoctorItem> Items,
    string? Note = null,
    string? ActionId = null)
{
    public DoctorLevel Level
    {
        get
        {
            if (Items.Any(i => i.Level == DoctorLevel.Missing))
                return DoctorLevel.Missing;
            if (Items.Any(i => i.Level == DoctorLevel.Warn))
                return DoctorLevel.Warn;
            if (Items.Any(i => i.Level == DoctorLevel.Ok))
                return DoctorLevel.Ok;
            return DoctorLevel.Info;
        }
    }

    public string Badge
    {
        get
        {
            var missing = Items.Count(i => i.Level == DoctorLevel.Missing);
            var warn = Items.Count(i => i.Level == DoctorLevel.Warn);
            if (missing > 0)
                return missing + " 項缺少";
            if (warn > 0)
                return warn + " 項需注意";
            return Level switch
            {
                DoctorLevel.Ok => "正常",
                DoctorLevel.Info when Id == "project" && Items.Any(i => i.Label == "專案") => "未選擇",
                _ => "",
            };
        }
    }
}

public sealed record DoctorSnapshot(IReadOnlyList<DoctorSection> Sections, string Text)
{
    public IEnumerable<DoctorItem> AllItems => Sections.SelectMany(s => s.Items);
    public int WarnCount => AllItems.Count(i => i.Level == DoctorLevel.Warn);
    public int MissingCount => AllItems.Count(i => i.Level == DoctorLevel.Missing);
    public DoctorLevel Overall =>
        MissingCount > 0 ? DoctorLevel.Missing :
        WarnCount > 0 ? DoctorLevel.Warn :
        DoctorLevel.Ok;

    public string Headline
    {
        get
        {
            if (MissingCount > 0 && WarnCount > 0)
                return $"{MissingCount} 項缺少、{WarnCount} 項需注意";
            if (MissingCount > 0)
                return $"{MissingCount} 項缺少";
            if (WarnCount > 0)
                return $"{WarnCount} 項需注意";
            return "環境正常";
        }
    }

    public string HeadlineDetail
    {
        get
        {
            var issues = AllItems
                .Where(i => i.Level is DoctorLevel.Missing or DoctorLevel.Warn)
                .Select(i => i.Label)
                .ToList();
            if (issues.Count > 0)
                return string.Join("、", issues);
            return "基本工具與目前 Agent 都可用。";
        }
    }

    public string ToText() => Text;

    public static DoctorSnapshot Build(ProjectCatalog? catalog)
    {
        var hasDotnet = CliUtil.CommandExists("dotnet");
        var hasGit = CliUtil.CommandExists("git");
        var hasGh = CliUtil.CommandExists("gh");
        var installKind = SelfUpdate.DetectInstallKind();
        var agent = AgentBackendRegistry.Inspect();
        var agentCli = CommitMessageSuggester.InspectCli();
        var mcpExe = McpLaunch.FindMcpExecutable();
        var mcpProj = McpLaunch.FindMcpProject();
        var mcpPolicy = catalog is null ? null : McpPolicy.Load(catalog.Root);
        var docfx = DocsService.InspectDocfx(catalog?.Root);
        var docs = string.IsNullOrWhiteSpace(catalog?.Root) ? null : DocsService.Scan(catalog.Root);

        var tools = new List<DoctorItem>
        {
            new("控制台", $"{AppInfo.Version} · {InstallKindLabel(installKind)}", DoctorLevel.Info),
            new(".NET", Environment.Version.ToString(), DoctorLevel.Info),
            Tool("dotnet", hasDotnet),
            Tool("git", hasGit),
            new("gh", hasGh ? "已安裝" : "未安裝（GitHub CLI，選用）",
                hasGh ? DoctorLevel.Ok : DoctorLevel.Info,
                Badge: hasGh ? "正常" : "選用"),
        };

        var agentItems = new List<DoctorItem>
        {
            new("目前後端", agent.CurrentName,
                agent.Available ? DoctorLevel.Ok : DoctorLevel.Missing,
                Detail: PathDetail(agent.Summary, agent.CliPath),
                Badge: agent.Available ? "可用" : "不可用"),
        };
        agentItems.Add(AgentCliItem(agentCli));
        if (agent.InstalledOthers.Count > 0)
            agentItems.Add(new("其他已安裝", string.Join("、", agent.InstalledOthers), DoctorLevel.Info));
        if (agent.MissingOthers.Count > 0)
            agentItems.Add(new("未偵測", string.Join("、", agent.MissingOthers), DoctorLevel.Info));

        var mcpItems = new List<DoctorItem>
        {
            McpServerItem(mcpExe, mcpProj),
        };
        if (mcpPolicy is not null)
        {
            mcpItems.Add(new(
                "MCP 政策",
                mcpPolicy.ReadOnly ? "唯讀" : "可寫",
                DoctorLevel.Info,
                $"允許 {(mcpPolicy.Allow.Count == 0 ? "全部" : string.Join("、", mcpPolicy.Allow))}；禁止 {(mcpPolicy.Deny.Count == 0 ? "無" : string.Join("、", mcpPolicy.Deny))}；需確認 {(mcpPolicy.Confirm.Count == 0 ? "無" : string.Join("、", mcpPolicy.Confirm))}（{mcpPolicy.Source}）"));
        }

        var sections = new List<DoctorSection>
        {
            new("tools", "基本工具", tools),
            new("agent", "Agent 後端", agentItems, "可在設定切換目前後端。", "prefs-agent"),
            new("mcp", "MCP", mcpItems, "寫入 .cursor/mcp.json、調整允許的工具。", "prefs-mcp"),
        };

        if (catalog is null)
        {
            sections.Add(new(
                "project",
                "專案與文件",
                [
                    new(
                        "專案",
                        "尚未選擇專案目錄。選好後會一併檢查文件工具（docfx）；若缺少會在同一區塊提供安裝步驟。",
                        DoctorLevel.Info,
                        HowTo: "先選專案，再決定要不要安裝 DocFX。這兩件事是同一條流程。"),
                ],
                ActionId: "pick-project"));
        }
        else
        {
            var docsItems = new List<DoctorItem>
            {
                new(
                    "docfx",
                    docfx.Label,
                    docfx.Available ? DoctorLevel.Ok : DoctorLevel.Warn,
                    HowTo: docfx.HowTo,
                    Badge: docfx.Badge),
            };
            if (docs is not null)
            {
                docsItems.Add(new(
                    "docs/",
                    docs.Label() + (docs.FileCount > 0 ? $"（{docs.FileCount} 檔）" : ""),
                    docs.Health switch
                    {
                        DocsHealth.Ready => DoctorLevel.Ok,
                        DocsHealth.Missing => DoctorLevel.Info,
                        _ => DoctorLevel.Warn,
                    },
                    Badge: docs.Health == DocsHealth.Ready ? "就緒" : docs.Label()));
                docsItems.Add(new(
                    "Pages workflow",
                    docs.HasWorkflow ? "已設定" : "尚未設定",
                    docs.HasWorkflow ? DoctorLevel.Ok : DoctorLevel.Info,
                    Badge: docs.HasWorkflow ? "有" : "無"));
            }

            var project = new List<DoctorItem>
            {
                new("名稱", catalog.Name, DoctorLevel.Ok, Badge: "已載入"),
                new("路徑", catalog.Root, DoctorLevel.Info, Detail: catalog.Root),
            };
            if (!string.IsNullOrWhiteSpace(catalog.Summary))
                project.Add(new("摘要", catalog.Summary, DoctorLevel.Info));
            project.Add(new(
                "服務",
                catalog.Services.Count == 0 ? "沒有服務或 UI" : catalog.Services.Count + " 個",
                DoctorLevel.Info,
                catalog.Services.Count == 0
                    ? null
                    : string.Join("\n", catalog.Services.Select(FormatService))));
            project.Add(new(
                "manifest",
                catalog.Manifest.Count > 0 ? "ai-project.json 已載入" : "無（使用掃描結果）",
                DoctorLevel.Info));
            var ci = CiWorkflow.Describe(catalog.Root);
            project.Add(new(
                "CI workflow",
                ci.Label,
                ci.BuildTest ? DoctorLevel.Ok : DoctorLevel.Info,
                HowTo: ci.BuildTest
                    ? null
                    : "遠端建置／測試請放 .github/workflows/ci.yml。控制台只顯示狀態，不代跑 Actions。",
                Badge: ci.Badge));

            sections.Add(new(
                "docs",
                "文件",
                docsItems,
                ActionId: docfx.Available || docfx.Kind == DocfxDetectKind.NoDotnet ? null : "install-docfx"));
            sections.Add(new("project", "專案", project));
        }

        return new DoctorSnapshot(sections, FormatText(catalog, agent, agentCli, mcpPolicy));
    }

    private static DoctorItem Tool(string name, bool ok) =>
        new(name, ok ? "已安裝" : "缺少", ok ? DoctorLevel.Ok : DoctorLevel.Missing);

    private static DoctorItem AgentCliItem(AgentCliDoctor cli)
    {
        if (!cli.Installed)
            return new("Cursor Agent CLI", "未安裝（選用；提交建議會改依 diff 產生）", DoctorLevel.Info, Badge: "選用");
        if (cli.NeedsLogin)
            return new("Cursor Agent CLI", "已安裝但未登入，請在終端機執行 agent login", DoctorLevel.Warn, cli.Path, "未登入");
        return new("Cursor Agent CLI", "已安裝", DoctorLevel.Ok, cli.Path, "正常");
    }

    private static DoctorItem McpServerItem(string? exe, string? proj)
    {
        if (exe is not null)
            return new("MCP 伺服器", "可用", DoctorLevel.Ok, exe, "正常");
        if (proj is not null)
            return new("MCP 伺服器", "開發中可用（從原始碼啟動）", DoctorLevel.Ok, proj, "正常");
        return new(
            "MCP 伺服器",
            "可用 dotnet run",
            DoctorLevel.Info,
            "dotnet run --project src/AiProject.Console.Mcp",
            "可用");
    }

    private static string InstallKindLabel(InstallKind kind) => kind switch
    {
        InstallKind.Development => "開發版",
        InstallKind.Installed => "已安裝",
        InstallKind.Portable => "可攜版",
        _ => kind.ToString(),
    };

    private static string? PathDetail(string summary, string? cliPath)
    {
        if (!string.IsNullOrWhiteSpace(cliPath))
            return cliPath;
        var trimmed = summary.Trim();
        const string prefix = "可用 — ";
        if (trimmed.StartsWith(prefix, StringComparison.Ordinal) && trimmed.Length > prefix.Length)
            return trimmed[prefix.Length..];
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatService(ServiceEntry svc)
    {
        var port = svc.Port?.ToString() ?? "-";
        var pre = string.IsNullOrEmpty(svc.PreStart) ? "" : $" · preStart {svc.PreStart}";
        return $"{svc.Label}  port {port}{pre}";
    }

    private static string FormatText(
        ProjectCatalog? catalog,
        AgentDoctorInfo agent,
        AgentCliDoctor agentCli,
        McpPolicy? mcpPolicy)
    {
        var lines = new List<string>
        {
            "AI_Project 環境體檢",
            "",
            $".NET: {Environment.Version}",
            $"控制台: {AppInfo.Version}（{SelfUpdate.DetectInstallKind()}）",
            $"dotnet: {(CliUtil.CommandExists("dotnet") ? "OK" : "缺少")}",
            $"git: {(CliUtil.CommandExists("git") ? "OK" : "缺少")}",
            $"gh: {(CliUtil.CommandExists("gh") ? "OK" : "缺少（GitHub CLI，選用）")}",
            "",
            $"目前 Agent 後端：{agent.CurrentName}（{(agent.Available ? "可用" : "不可用")}）",
            "  " + agent.Summary,
        };
        if (agent.InstalledOthers.Count > 0)
            lines.Add("其他已安裝：" + string.Join("、", agent.InstalledOthers));
        if (agent.MissingOthers.Count > 0)
            lines.Add("未偵測：" + string.Join("、", agent.MissingOthers));
        lines.Add("可在「設定」切換後端。");
        lines.Add(agentCli.Line);
        lines.Add(McpLaunch.DoctorLine());
        if (mcpPolicy is not null)
            lines.Add(mcpPolicy.DoctorLine());
        lines.Add(DocsService.DoctorLine(catalog?.Root));
        if (catalog is null)
        {
            lines.Add("");
            lines.Add("尚未選擇專案目錄。");
            return string.Join('\n', lines);
        }

        lines.AddRange(
        [
            "",
            $"專案：{catalog.Name}",
            $"路徑：{catalog.Root}",
            $"摘要：{catalog.Summary}",
            $"服務：{catalog.Services.Count}",
        ]);
        foreach (var svc in catalog.Services)
        {
            var port = svc.Port?.ToString() ?? "-";
            var pre = string.IsNullOrEmpty(svc.PreStart) ? "" : $" preStart={svc.PreStart}";
            lines.Add($"  - {svc.Label} [{svc.Id}] port={port}{pre} ({svc.Source})");
        }
        lines.Add(catalog.Manifest.Count > 0 ? "manifest: ai-project.json 已載入" : "manifest: 無（使用掃描結果）");
        lines.Add(CiWorkflow.DoctorLine(catalog.Root));
        return string.Join('\n', lines);
    }
}
