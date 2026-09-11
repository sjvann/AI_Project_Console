using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Stack;
using AiProject.Console.Core.Tech;
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
        var hasGit = CliUtil.CommandExists("git");
        var hasGh = CliUtil.CommandExists("gh");
<<<<<<< HEAD
        var hasPython = ProcessSupervisor.HasPythonLauncher();
=======
        var hasWinget = ToolchainBootstrap.HasWinget();
>>>>>>> f55e2ad032f0c6166b24b0d4da0ab3b5841f0927
        var installKind = SelfUpdate.DetectInstallKind();
        var agent = AgentBackendRegistry.Inspect();
        var agentCli = CommitMessageSuggester.InspectCli();
        var mcpHost = McpLaunch.FindProductHost() ?? McpLaunch.FindMcpExecutable();
        var mcpProj = McpLaunch.FindMcpProject();
        var mcpPolicy = catalog is null ? null : McpPolicy.Load(catalog.Root);
        var docfx = DocsService.InspectDocfx(catalog?.Root);
        var docs = string.IsNullOrWhiteSpace(catalog?.Root) ? null : DocsService.Scan(catalog.Root);
        var stackIds = TechStackDetector.RequiredStacks(catalog);
        var toolIds = TechStackCatalog.RequiredToolIdsFor(stackIds);
        var toolStatus = ToolchainBootstrap.Inspect(toolIds);

        var tools = new List<DoctorItem>
        {
            new("控制台", $"{AppInfo.Version} · {InstallKindLabel(installKind)}", DoctorLevel.Info),
<<<<<<< HEAD
            new(".NET", Environment.Version.ToString(), DoctorLevel.Info),
            Tool("dotnet", hasDotnet),
            Tool("python", hasPython),
=======
            new(".NET 執行環境", Environment.Version.ToString(), DoctorLevel.Info),
>>>>>>> f55e2ad032f0c6166b24b0d4da0ab3b5841f0927
            Tool("git", hasGit),
            new("gh", hasGh ? "已安裝" : "未安裝（GitHub CLI，選用）",
                hasGh ? DoctorLevel.Ok : DoctorLevel.Info,
                Badge: hasGh ? "正常" : "選用"),
            new("winget", hasWinget ? "可用（用來安裝語言環境）" : "未安裝（缺少時會開官方下載頁）",
                DoctorLevel.Info,
                Badge: hasWinget ? "可用" : "選用"),
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
            McpServerItem(mcpHost, mcpProj),
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
        };
        if (catalog is not null)
        {
            var toolchainItems = BuildToolchainItems(catalog, stackIds, toolStatus);
            var missingTools = toolchainItems.Count(i => i.Level == DoctorLevel.Missing);
            sections.Add(new(
                "toolchain",
                "開發環境",
                toolchainItems,
                missingTools > 0
                    ? "缺少的執行環境可按下方按鈕安裝（Windows 用 winget；沒有 winget 會開官方下載頁）。裝好後請再按一次環境體檢。"
                    : "依專案檔與副檔名偵測。套件還原可在缺少 node_modules／.venv 時執行。",
                missingTools > 0 ? "install-toolchains" : null));
        }
        sections.Add(new("agent", "Agent 後端", agentItems, "可在設定切換目前後端。", "prefs-agent"));
        sections.Add(new("mcp", "MCP", mcpItems, "寫入 .cursor/mcp.json、調整允許的工具。", "prefs-mcp"));

        if (catalog is null)
        {
            sections.Add(new(
                "project",
                "專案與文件",
                [
                    new(
                        "專案",
                        "尚未選擇專案目錄。選好後會依專案檔／副檔名檢查對應的編譯環境，並一併檢查文件工具（docfx）。",
                        DoctorLevel.Info,
                        HowTo: "先選專案。控制台會偵測 .NET、Node、Python、Go、Java、Rust 等常見語言，缺少時可在體檢裡安裝。"),
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
            var langs = catalog.Projects
                .Select(p => p.Language)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (langs.Count > 0)
                project.Add(new("語言", string.Join("、", langs), DoctorLevel.Info));
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
                    : "GitHub 操作台「補齊 CI workflow…」可寫入骨架。控制台只顯示狀態，不代跑 Actions。",
                Badge: ci.Badge));

            sections.Add(new(
                "docs",
                "文件",
                docsItems,
                ActionId: docfx.Available || docfx.Kind == DocfxDetectKind.NoDotnet ? null : "install-docfx"));
            sections.Add(new("project", "專案", project));
        }

        return new DoctorSnapshot(sections, FormatText(catalog, agent, agentCli, mcpPolicy, stackIds, toolStatus));
    }

    static List<DoctorItem> BuildToolchainItems(
        ProjectCatalog catalog,
        IReadOnlyList<string> stackIds,
        IReadOnlyList<ToolStatus> toolStatus)
    {
        var items = new List<DoctorItem>();
        var langs = catalog.Projects
            .Select(p => string.IsNullOrEmpty(p.Language) ? p.StackId : p.Language)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        items.Add(new(
            "偵測到",
            langs.Count == 0 && stackIds.Count == 0
                ? "尚未辨識到專案檔或常見原始碼（仍可手動編 ai-project.json）"
                : string.Join("、", langs.Count > 0 ? langs : stackIds),
            langs.Count + stackIds.Count == 0 ? DoctorLevel.Info : DoctorLevel.Ok,
            HowTo: "依據 package.json、pyproject.toml、go.mod、Cargo.toml、pom.xml、*.csproj 等專案檔，或 .py／.ts／.go 等副檔名。"));

        foreach (var st in toolStatus)
        {
            if (st.Installed)
            {
                items.Add(new(
                    st.Spec.DisplayName,
                    "已安裝",
                    DoctorLevel.Ok,
                    Detail: st.ResolvedPath,
                    Badge: "正常"));
                continue;
            }
            items.Add(new(
                st.Spec.DisplayName,
                "缺少",
                DoctorLevel.Missing,
                HowTo: st.Spec.HowTo,
                Badge: "缺少",
                ActionId: "install-toolchain:" + st.Spec.Id,
                ActionLabel: "安裝 " + st.Spec.DisplayName));
        }

        foreach (var p in catalog.Projects)
        {
            if (string.IsNullOrEmpty(p.StackId) || p.StackId == "dotnet")
                continue;
            var dir = Path.Combine(catalog.Root, p.RelDir.Replace('/', Path.DirectorySeparatorChar));
            if (StackCommands.PackagesRestored(dir, p.StackId))
                continue;
            if (!Directory.Exists(dir))
                continue;
            items.Add(new(
                p.Name + " 套件",
                "尚未還原",
                DoctorLevel.Warn,
                HowTo: "安裝執行環境後可按「還原套件」（npm install／pip install 等）。",
                Badge: "需還原",
                ActionId: "restore-packages:" + p.RelDir,
                ActionLabel: "還原套件"));
        }
        return items;
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
            return new("MCP 伺服器", "本控制台", DoctorLevel.Ok, exe, "正常");
        if (proj is not null)
            return new("MCP 伺服器", "開發中可用（從原始碼啟動）", DoctorLevel.Ok, proj, "正常");
        return new(
            "MCP 伺服器",
            "請從本控制台寫入 mcp.json",
            DoctorLevel.Info,
            "指向 AI_Project 控制台（--mcp），不是受管理專案的 src",
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
        var deps = svc.Dependencies.Count == 0
            ? ""
            : " · 依 " + string.Join("、", svc.Dependencies.Select(d => d.Optional ? d.Id + "?" : d.Id));
        return $"{svc.Label}  port {port}{pre}{deps}";
    }

    private static string FormatText(
        ProjectCatalog? catalog,
        AgentDoctorInfo agent,
        AgentCliDoctor agentCli,
        McpPolicy? mcpPolicy,
        IReadOnlyList<string> stackIds,
        IReadOnlyList<ToolStatus> toolStatus)
    {
        var lines = new List<string>
        {
            "AI_Project 環境體檢",
            "",
            $".NET: {Environment.Version}",
            $"控制台: {AppInfo.Version}（{SelfUpdate.DetectInstallKind()}）",
<<<<<<< HEAD
            $"dotnet: {(CliUtil.CommandExists("dotnet") ? "OK" : "缺少")}",
            $"python: {(ProcessSupervisor.HasPythonLauncher() ? "OK" : "缺少（preStart .py 需要）")}",
=======
>>>>>>> f55e2ad032f0c6166b24b0d4da0ab3b5841f0927
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

        if (stackIds.Count > 0 || toolStatus.Count > 0)
        {
            lines.Add("");
            lines.Add("開發環境：" + (stackIds.Count == 0 ? "（依掃描）" : string.Join("、", stackIds)));
            foreach (var st in toolStatus)
                lines.Add($"  - {st.Spec.DisplayName}: {(st.Installed ? "OK" : "缺少")}");
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
            var deps = svc.Dependencies.Count == 0
                ? ""
                : " dependsOn=" + string.Join(",", svc.Dependencies.Select(d => d.Optional ? d.Id + "?" : d.Id));
            lines.Add($"  - {svc.Label} [{svc.Id}] port={port}{pre}{deps} ({svc.Source})");
        }
        lines.Add(catalog.Manifest.Count > 0 ? "manifest: ai-project.json 已載入" : "manifest: 無（使用掃描結果）");
        lines.Add(CiWorkflow.DoctorLine(catalog.Root));
        return string.Join('\n', lines);
    }
}
