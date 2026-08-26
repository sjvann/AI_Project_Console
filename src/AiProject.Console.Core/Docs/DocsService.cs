using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Docs;

public static class DocsService
{
    public const string FolderName = "docs";
    public const string TodoMarker = "待補";
    public const string DefaultServeUrl = "http://127.0.0.1:8080/";
    public const string WorkflowRelPath = ".github/workflows/docs.yml";
    public const string ToolsManifestRelPath = ".config/dotnet-tools.json";
    public const string DocfxToolVersion = "2.78.5";

    public static readonly IReadOnlyList<string> ScaffoldFiles =
    [
        "README.md",
        "toc.yml",
        "docfx.json",
        "user/getting-started.md",
        "user/daily-use.md",
        "user/troubleshooting.md",
        "product/overview.md",
        "product/glossary.md",
        "engineering/architecture.md",
        "engineering/develop.md",
        "operations/deploy.md",
    ];

    static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".yml", ".yaml", ".json",
    };

    static readonly HashSet<string> SkipDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_site", "api", "obj", "bin", ".git",
    };

    public static string DocsDirectory(string projectRoot)
    {
        return FindDocsDirectory(projectRoot)
            ?? Path.Combine(Path.GetFullPath(projectRoot), FolderName);
    }

    public static string? FindDocsDirectory(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return null;
        var root = Path.GetFullPath(projectRoot);
        foreach (var name in new[] { FolderName, "Docs" })
        {
            var dir = Path.Combine(root, name);
            if (Directory.Exists(dir))
                return dir;
        }
        return null;
    }

    public static bool HasDocsFolder(string projectRoot) => FindDocsDirectory(projectRoot) is not null;

    public static string ResolveInsideDocs(string projectRoot, string relPath)
    {
        var docs = DocsDirectory(projectRoot);
        if (!IsSafeRelPath(relPath))
            throw new InvalidOperationException("路徑無效。只能使用 docs/ 內的相對路徑。");
        var rel = NormalizeRel(relPath);
        if (rel.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidOperationException("路徑無效。只能使用 docs/ 內的相對路徑。");
        var ext = Path.GetExtension(rel);
        if (!AllowedExt.Contains(ext))
            throw new InvalidOperationException("只能讀寫 Markdown 或 DocFX 設定（.md／.yml／.json）。");
        Directory.CreateDirectory(docs);
        var full = Path.GetFullPath(Path.Combine(docs, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsUnder(full, docs))
            throw new InvalidOperationException("路徑超出 docs/。");
        return full;
    }

    public static bool IsSafeRelPath(string? relPath)
    {
        try
        {
            var raw = (relPath ?? "").Trim();
            if (string.IsNullOrEmpty(raw))
                return false;
            if (raw.StartsWith('/') || raw.StartsWith('\\'))
                return false;
            var asOs = raw.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(asOs))
                return false;
            var rel = NormalizeRel(raw);
            if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal))
                return false;
            return AllowedExt.Contains(Path.GetExtension(rel));
        }
        catch
        {
            return false;
        }
    }

    public static string Read(string projectRoot, string relPath)
    {
        var full = ResolveInsideDocs(projectRoot, relPath);
        return File.Exists(full) ? File.ReadAllText(full, Encoding.UTF8) : "";
    }

    public static void Write(string projectRoot, string relPath, string content)
    {
        var full = ResolveInsideDocs(projectRoot, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static DocsStatus Scan(string projectRoot)
    {
        var root = Path.GetFullPath(projectRoot);
        var docs = FindDocsDirectory(root);
        if (docs is null)
        {
            return new DocsStatus(
                root,
                Path.Combine(root, FolderName),
                DocsHealth.Missing,
                0,
                0,
                false,
                File.Exists(Path.Combine(root, "docfx.json")),
                HasWorkflow(root),
                ScaffoldFiles.ToList(),
                []);
        }

        var files = ListFiles(docs);
        var hasToc = files.Any(f => string.Equals(f.RelPath, "toc.yml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.RelPath, "toc.yaml", StringComparison.OrdinalIgnoreCase));
        var hasDocfx = File.Exists(Path.Combine(docs, "docfx.json"))
            || File.Exists(Path.Combine(root, "docfx.json"));
        var present = new HashSet<string>(files.Select(f => f.RelPath), StringComparer.OrdinalIgnoreCase);
        var missing = ScaffoldFiles.Where(p => !present.Contains(p)).ToList();
        var stubCount = files.Count(f => f.IsStub);
        var mdCount = files.Count(f => f.RelPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        DocsHealth health;
        if (mdCount == 0 && !hasToc && !hasDocfx)
            health = DocsHealth.Missing;
        else if (!hasToc || !hasDocfx)
            health = DocsHealth.Incomplete;
        else if (stubCount > 0)
            health = DocsHealth.Draft;
        else
            health = DocsHealth.Ready;

        return new DocsStatus(
            root,
            docs,
            health,
            files.Count,
            stubCount,
            hasToc,
            hasDocfx,
            HasWorkflow(root),
            missing,
            files);
    }

    public static DocsScaffoldResult Scaffold(string projectRoot, DocsScaffoldContext? context = null)
    {
        var root = Path.GetFullPath(projectRoot);
        var docs = DocsDirectory(root);
        Directory.CreateDirectory(docs);
        context ??= new DocsScaffoldContext(Path.GetFileName(root), root, [], [], ReadRootReadmeExcerpt(root), null);
        var created = new List<string>();
        var skipped = new List<string>();
        foreach (var rel in ScaffoldFiles)
        {
            var full = ResolveInsideDocs(root, rel);
            if (File.Exists(full) && new FileInfo(full).Length > 0)
            {
                skipped.Add(rel);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, RenderScaffoldPage(rel, context), new UTF8Encoding(false));
            created.Add(rel);
        }

        var extra = new List<string>();
        if (EnsureToolsManifest(root))
            extra.Add(ToolsManifestRelPath);
        if (EnsureWorkflow(root))
            extra.Add(WorkflowRelPath);
        created.AddRange(extra);

        var msg = created.Count == 0
            ? "文件體系已存在，未覆蓋任何檔案。"
            : $"已建立 {created.Count} 個檔案（既有 {skipped.Count} 個未覆蓋）。";
        return new DocsScaffoldResult(created, skipped, msg);
    }

    public static bool EnsureWorkflow(string projectRoot)
    {
        var path = Path.Combine(Path.GetFullPath(projectRoot), WorkflowRelPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, WorkflowTemplate(), new UTF8Encoding(false));
        return true;
    }

    public static bool EnsureToolsManifest(string projectRoot)
    {
        var path = Path.Combine(Path.GetFullPath(projectRoot), ToolsManifestRelPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToolsManifestTemplate(), new UTF8Encoding(false));
        return true;
    }

    public static bool HasWorkflow(string projectRoot) =>
        File.Exists(Path.Combine(Path.GetFullPath(projectRoot), WorkflowRelPath.Replace('/', Path.DirectorySeparatorChar)));

    public static string? FindDocfxJson(string projectRoot)
    {
        var docs = FindDocsDirectory(projectRoot);
        if (docs is not null)
        {
            var nested = Path.Combine(docs, "docfx.json");
            if (File.Exists(nested))
                return nested;
        }
        var root = Path.Combine(Path.GetFullPath(projectRoot), "docfx.json");
        return File.Exists(root) ? root : null;
    }

    public static string WorkflowTemplate() =>
        """
        name: Deploy docs

        on:
          push:
            branches: [main]
          pull_request:
            branches: [main]
          workflow_dispatch:

        permissions:
          contents: read
          pages: write
          id-token: write

        concurrency:
          group: pages
          cancel-in-progress: false

        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - name: Checkout
                uses: actions/checkout@v4

              - name: Setup .NET
                uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: "10.0.x"

              - name: Restore DocFX
                run: dotnet tool restore

              - name: Build site
                run: dotnet docfx docs/docfx.json

              - name: Upload Pages artifact
                if: github.event_name != 'pull_request'
                uses: actions/upload-pages-artifact@v3
                with:
                  path: docs/_site

          deploy:
            if: github.event_name != 'pull_request'
            needs: build
            runs-on: ubuntu-latest
            environment:
              name: github-pages
              url: ${{ steps.deployment.outputs.page_url }}
            steps:
              - name: Deploy to GitHub Pages
                id: deployment
                uses: actions/deploy-pages@v4
        """.Replace("\r\n", "\n") + "\n";

    public static string ToolsManifestTemplate() =>
        """
        {
          "version": 1,
          "isRoot": true,
          "tools": {
            "docfx": {
              "version": "2.78.5",
              "commands": [
                "docfx"
              ]
            }
          }
        }
        """.Replace("\r\n", "\n") + "\n";

    public static string PagesUrl(GithubConfig? cfg)
    {
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Owner) || string.IsNullOrWhiteSpace(cfg.Repo))
            return "";
        return $"https://{cfg.Owner.ToLowerInvariant()}.github.io/{cfg.Repo}/";
    }

    public static async Task<string> EnablePagesAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        if (string.IsNullOrEmpty(cfg.Slug()))
            throw new InvalidOperationException("請先完成 GitHub 設定（owner/repo）。");
        EnsureWorkflow(catalog.Root);
        EnsureToolsManifest(catalog.Root);

        var slug = cfg.Slug();
        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["api", "--method", "POST", $"/repos/{slug}/pages", "-f", "build_type=workflow"],
            catalog.Root,
            60_000).ConfigureAwait(false);
        if (code != 0)
        {
            var (c2, out2) = await CliUtil.RunAsync(
                "gh",
                ["api", "--method", "PUT", $"/repos/{slug}/pages", "-f", "build_type=workflow"],
                catalog.Root,
                60_000).ConfigureAwait(false);
            if (c2 != 0)
            {
                var hint = "請到 GitHub → Settings → Pages，Source 選 GitHub Actions。";
                throw new InvalidOperationException(
                    (string.IsNullOrEmpty(output) ? out2 : output) + "\n\n" + hint);
            }
            output = string.IsNullOrEmpty(out2) ? output : out2;
        }
        var url = ExtractPagesHtmlUrl(output) ?? PagesUrl(cfg);
        return string.IsNullOrEmpty(url)
            ? "已啟用 GitHub Pages（Actions）。推送到 main 後會發佈。"
            : "已啟用 GitHub Pages（Actions）：" + url;
    }

    public static async Task<string> PagesStatusAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var guessed = PagesUrl(cfg);
        if (!GitHubService.GhAvailable() || string.IsNullOrEmpty(cfg.Slug()))
            return string.IsNullOrEmpty(guessed) ? "尚未設定 GitHub，無法判斷 Pages。" : "推測網址：" + guessed;
        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["api", $"/repos/{cfg.Slug()}/pages"],
            catalog.Root,
            60_000).ConfigureAwait(false);
        if (code != 0)
            return string.IsNullOrEmpty(guessed)
                ? (string.IsNullOrEmpty(output) ? "尚未啟用 GitHub Pages。" : output)
                : "尚未啟用 GitHub Pages。推測網址：" + guessed;
        var url = ExtractPagesHtmlUrl(output) ?? guessed;
        return string.IsNullOrEmpty(url) ? output : url;
    }

    public static bool OpenPages(GithubConfig? cfg, string? htmlUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(htmlUrl) ? PagesUrl(cfg) : htmlUrl.Trim();
        if (string.IsNullOrEmpty(url))
            return false;
        CliUtil.OpenUrl(url);
        return true;
    }

    public static string DoctorLine(string? projectRoot)
    {
        var parts = new List<string>
        {
            "dotnet: " + (CliUtil.CommandExists("dotnet") ? "OK" : "缺少"),
            "docfx: " + (DocfxAvailable(projectRoot) ? "OK" : "缺少（將用 dotnet tool restore）"),
        };
        if (string.IsNullOrWhiteSpace(projectRoot))
            return "文件：" + string.Join("；", parts);
        var status = Scan(projectRoot);
        parts.Add("docs: " + status.Label() + (status.FileCount > 0 ? $"（{status.FileCount} 檔）" : ""));
        parts.Add("Pages workflow: " + (status.HasWorkflow ? "有" : "無"));
        return "文件：" + string.Join("；", parts);
    }

    public static bool DocfxAvailable(string? projectRoot)
    {
        if (CliUtil.CommandExists("docfx"))
            return true;
        if (!CliUtil.CommandExists("dotnet"))
            return false;
        var root = string.IsNullOrWhiteSpace(projectRoot) ? null : Path.GetFullPath(projectRoot);
        if (root is not null && File.Exists(Path.Combine(root, ToolsManifestRelPath.Replace('/', Path.DirectorySeparatorChar))))
            return true;
        return false;
    }

    public static async Task<string> BuildAsync(string projectRoot, int timeoutMs = 180_000)
    {
        var json = FindDocfxJson(projectRoot)
            ?? throw new InvalidOperationException("找不到 docfx.json。請先「建立／補齊體系」。");
        await RestoreDocfxAsync(projectRoot).ConfigureAwait(false);
        var cwd = Path.GetDirectoryName(json) ?? projectRoot;
        var (code, output) = await CliUtil.RunAsync("dotnet", ["docfx", json], cwd, timeoutMs).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "DocFX 建置失敗。" : output);
        return string.IsNullOrEmpty(output) ? "DocFX 建置完成。" : output;
    }

    public static async Task<DocsServeHandle> ServeAsync(string projectRoot, int port = 8080)
    {
        var json = FindDocfxJson(projectRoot)
            ?? throw new InvalidOperationException("找不到 docfx.json。請先「建立／補齊體系」。");
        if (!CliUtil.CommandExists("dotnet"))
            throw new InvalidOperationException("找不到 dotnet。請安裝 .NET SDK。");
        await RestoreDocfxAsync(projectRoot).ConfigureAwait(false);
        var cwd = Path.GetDirectoryName(json) ?? projectRoot;
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("docfx");
        psi.ArgumentList.Add(json);
        psi.ArgumentList.Add("--serve");
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("無法啟動 DocFX。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            proc.Dispose();
            throw new InvalidOperationException("無法啟動 DocFX：" + ex.Message);
        }
        return new DocsServeHandle(proc, $"http://127.0.0.1:{port}/");
    }

    public static string ReadRootReadmeExcerpt(string projectRoot, int maxChars = 800)
    {
        var path = Path.Combine(Path.GetFullPath(projectRoot), "README.md");
        if (!File.Exists(path))
            return "";
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8).Trim();
            if (text.Length <= maxChars)
                return text;
            return text[..maxChars].TrimEnd() + "…";
        }
        catch
        {
            return "";
        }
    }

    public static string TitleOf(string relPath, string? content)
    {
        if (!string.IsNullOrEmpty(content))
        {
            var fm = Regex.Match(content, @"^---\s*\r?\n(?:.*\r?\n)*?title:\s*(.+)\r?\n(?:.*\r?\n)*?---", RegexOptions.IgnoreCase);
            if (fm.Success)
                return fm.Groups[1].Value.Trim().Trim('"', '\'');
            var heading = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
            if (heading.Success)
                return heading.Groups[1].Value.Trim();
        }
        var name = Path.GetFileNameWithoutExtension(relPath.Replace('\\', '/'));
        return string.IsNullOrEmpty(name) ? relPath : name;
    }

    public static bool LooksLikeStub(string? content) =>
        !string.IsNullOrEmpty(content) && content.Contains(TodoMarker, StringComparison.Ordinal);

    static async Task RestoreDocfxAsync(string projectRoot)
    {
        var manifest = Path.Combine(Path.GetFullPath(projectRoot), ToolsManifestRelPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifest))
            return;
        await CliUtil.RunAsync("dotnet", ["tool", "restore"], projectRoot, 120_000).ConfigureAwait(false);
    }

    static List<DocsFile> ListFiles(string docsRoot)
    {
        var list = new List<DocsFile>();
        if (!Directory.Exists(docsRoot))
            return list;
        foreach (var full in Directory.EnumerateFiles(docsRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(docsRoot, full).Replace('\\', '/');
            if (rel.Split('/').Any(SkipDirNames.Contains))
                continue;
            var ext = Path.GetExtension(rel);
            if (!AllowedExt.Contains(ext))
                continue;
            string text;
            try { text = File.ReadAllText(full, Encoding.UTF8); }
            catch { continue; }
            var isConfig = ext is ".yml" or ".yaml" or ".json";
            list.Add(new DocsFile(rel, TitleOf(rel, text), !isConfig && LooksLikeStub(text), isConfig));
        }
        return list
            .OrderBy(f => f.IsConfig ? 1 : 0)
            .ThenBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static string RenderScaffoldPage(string rel, DocsScaffoldContext ctx)
    {
        return rel switch
        {
            "docfx.json" => DocfxJson(ctx.Name),
            "toc.yml" => TocYml(),
            "README.md" => Page(
                "使用文件",
                "文件地圖",
                MapBody(ctx)),
            "user/getting-started.md" => Page(
                "安裝與第一次使用",
                "安裝與第一次使用",
                Intro(ctx, "寫安裝、第一次開啟、必要工具。")),
            "user/daily-use.md" => Page(
                "日常操作",
                "日常操作",
                Intro(ctx, "寫每天重複的啟動、停止、編譯與查看方式。")),
            "user/troubleshooting.md" => Page(
                "常見問題",
                "常見問題",
                Intro(ctx, "寫最常卡住的情況與對應做法。")),
            "product/overview.md" => Page(
                "產品概述",
                "產品概述",
                Intro(ctx, "用一段話說明這份產品給誰用、解決什麼問題。") + ServicesBlock(ctx)),
            "product/glossary.md" => Page(
                "名詞",
                "名詞",
                Intro(ctx, "列出專案裡會反覆出現的詞。")),
            "engineering/architecture.md" => Page(
                "架構",
                "架構",
                Intro(ctx, "寫主要模組、資料流與相依。") + ProjectsBlock(ctx)),
            "engineering/develop.md" => Page(
                "開發與建置",
                "開發與建置",
                Intro(ctx, "寫本機建置、測試與目錄慣例。")),
            "operations/deploy.md" => Page(
                "部署",
                "部署",
                Intro(ctx, "寫發佈目標、環境與回滾。")),
            _ => Page(rel, Path.GetFileNameWithoutExtension(rel), Intro(ctx, "補上此頁說明。")),
        };
    }

    static string Page(string title, string heading, string body) =>
        $"---\ntitle: {title}\n---\n\n# {heading}\n\n{body.Trim()}\n";

    static string Intro(DocsScaffoldContext ctx, string hint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"（待補）{hint}");
        sb.AppendLine();
        sb.AppendLine($"專案：**{ctx.Name}**。");
        if (!string.IsNullOrWhiteSpace(ctx.ReadmeExcerpt))
        {
            sb.AppendLine();
            sb.AppendLine("倉根 README 摘錄：");
            sb.AppendLine();
            sb.AppendLine(ctx.ReadmeExcerpt);
        }
        return sb.ToString();
    }

    static string MapBody(DocsScaffoldContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"這是 **{ctx.Name}** 的文件地圖。內容都放在 `docs/`。");
        sb.AppendLine();
        sb.AppendLine("| 角色 | 從這裡開始 |");
        sb.AppendLine("|------|------------|");
        sb.AppendLine("| 一般使用者 | [安裝與第一次使用](user/getting-started.md) |");
        sb.AppendLine("| 了解產品 | [產品概述](product/overview.md) |");
        sb.AppendLine("| 開發 | [架構](engineering/architecture.md) |");
        sb.AppendLine("| 部署 | [部署](operations/deploy.md) |");
        if (!string.IsNullOrWhiteSpace(ctx.ReadmeExcerpt))
        {
            sb.AppendLine();
            sb.AppendLine("倉根 [README](../README.md) 只保留最短路徑。");
        }
        sb.AppendLine();
        sb.AppendLine("（待補）依實際角色再調這張表。");
        return sb.ToString();
    }

    static string ServicesBlock(DocsScaffoldContext ctx)
    {
        if (ctx.Services.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("目前掃描到的服務：");
        sb.AppendLine();
        foreach (var s in ctx.Services)
            sb.AppendLine("- " + s);
        return sb.ToString();
    }

    static string ProjectsBlock(DocsScaffoldContext ctx)
    {
        if (ctx.Projects.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("目前掃描到的編譯專案：");
        sb.AppendLine();
        foreach (var p in ctx.Projects)
            sb.AppendLine("- " + p);
        return sb.ToString();
    }

    static string TocYml() =>
        """
        - name: 使用文件
          href: README.md
        - name: 一般使用者
          items:
            - name: 安裝與第一次使用
              href: user/getting-started.md
            - name: 日常操作
              href: user/daily-use.md
            - name: 常見問題
              href: user/troubleshooting.md
        - name: 產品
          items:
            - name: 產品概述
              href: product/overview.md
            - name: 名詞
              href: product/glossary.md
        - name: 工程
          items:
            - name: 架構
              href: engineering/architecture.md
            - name: 開發與建置
              href: engineering/develop.md
        - name: 營運
          items:
            - name: 部署
              href: operations/deploy.md
        """.Replace("\r\n", "\n") + "\n";

    static string DocfxJson(string name)
    {
        var title = string.IsNullOrWhiteSpace(name) ? "使用文件" : name;
        var obj = new
        {
            build = new
            {
                content = new object[]
                {
                    new { files = new[] { "**/*.{md,yml}" } },
                },
                resource = new object[]
                {
                    new { files = new[] { "**/media/**" } },
                },
                output = "_site",
                template = new[] { "default", "modern" },
                globalMetadata = new Dictionary<string, object>
                {
                    ["_appName"] = title,
                    ["_appTitle"] = title,
                    ["_enableSearch"] = true,
                    ["_lang"] = "zh-Hant",
                },
            },
        };
        return JsonSerializer.Serialize(obj, JsonUtil.Options) + "\n";
    }

    static string? ExtractPagesHtmlUrl(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("html_url", out var u))
                return u.GetString();
        }
        catch (JsonException)
        {
            var m = Regex.Match(json, @"https://[^\s""']+\.github\.io/[^\s""']+");
            if (m.Success)
                return m.Value;
        }
        return null;
    }

    static string NormalizeRel(string? relPath) =>
        (relPath ?? "").Replace('\\', '/').Trim().TrimStart('/');

    static bool IsUnder(string full, string parent)
    {
        var a = Path.GetFullPath(full);
        var b = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
