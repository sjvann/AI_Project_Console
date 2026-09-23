using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Docs;

public static class DocsService
{
    public const string FolderName = "docs";
    /// <summary>骨架占位句。判定待補只認這組字，避免說明文提到摘要列「待補」也被算進去。</summary>
    public const string TodoMarker = "（待補）";
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
        "product/intake.md",
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

    public static bool Delete(string projectRoot, string relPath)
    {
        var full = ResolveInsideDocs(projectRoot, relPath);
        if (!File.Exists(full))
            return false;
        File.Delete(full);
        TryPruneEmptyParents(Path.GetDirectoryName(full), DocsDirectory(projectRoot));
        return true;
    }

    public static bool IsScaffoldFile(string? relPath)
    {
        var rel = NormalizeRel(relPath);
        return ScaffoldFiles.Any(s => string.Equals(s, rel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 骨架檔是否已滿足。倉根的 <c>docfx.json</c>（本控制台文件站）視同 <c>docs/docfx.json</c>。
    /// </summary>
    static bool IsScaffoldSatisfied(string rel, HashSet<string> present, string projectRoot)
    {
        if (present.Contains(rel))
            return true;
        return string.Equals(rel, "docfx.json", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(projectRoot, "docfx.json"));
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
        var missing = ScaffoldFiles.Where(p => !IsScaffoldSatisfied(p, present, root)).ToList();
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
        var intakePath = Path.Combine(docs, "product", "intake.json");
        if (!File.Exists(intakePath) || new FileInfo(intakePath).Length == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(intakePath)!);
            File.WriteAllText(intakePath, """
                {
                  "version": "1",
                  "designDocsDir": "docs/product/design",
                  "intakes": []
                }
                """, new UTF8Encoding(false));
            extra.Add("product/intake.json");
        }
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
        var (code, output) = await GhCli.RunAsync(
            ["api", "--method", "POST", $"/repos/{slug}/pages", "-f", "build_type=workflow"],
            catalog.Root,
            cfg,
            60_000).ConfigureAwait(false);
        if (code != 0)
        {
            var (c2, out2) = await GhCli.RunAsync(
                ["api", "--method", "PUT", $"/repos/{slug}/pages", "-f", "build_type=workflow"],
                catalog.Root,
                cfg,
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
        var (code, output) = await GhCli.RunAsync(
            ["api", $"/repos/{cfg.Slug()}/pages"],
            catalog.Root,
            cfg,
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
            "docfx: " + InspectDocfx(projectRoot).DoctorText,
        };
        if (string.IsNullOrWhiteSpace(projectRoot))
            return "文件：" + string.Join("；", parts);
        var status = Scan(projectRoot);
        parts.Add("docs: " + status.Label() + (status.FileCount > 0 ? $"（{status.FileCount} 檔）" : ""));
        parts.Add("Pages workflow: " + (status.HasWorkflow ? "有" : "無"));
        return "文件：" + string.Join("；", parts);
    }

    public static bool DocfxAvailable(string? projectRoot) => InspectDocfx(projectRoot).Available;

    /// <summary>
    /// 專案有工具清單時看該專案；未選專案時改看控制台所在倉庫（本機 <c>dotnet tool</c>，不是 PATH 上的 <c>docfx</c>）。
    /// </summary>
    public static DocfxDetect InspectDocfx(string? projectRoot)
    {
        if (CliUtil.CommandExists("docfx"))
            return new DocfxDetect(true, DocfxDetectKind.Command);
        if (!CliUtil.CommandExists("dotnet"))
            return new DocfxDetect(false, DocfxDetectKind.NoDotnet);
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            return HasToolsManifest(projectRoot)
                ? new DocfxDetect(true, DocfxDetectKind.ProjectManifest)
                : new DocfxDetect(false, DocfxDetectKind.Missing);
        }
        return FindHostToolsManifest() is not null
            ? new DocfxDetect(true, DocfxDetectKind.HostManifest)
            : new DocfxDetect(false, DocfxDetectKind.Missing);
    }

    public static bool HasToolsManifest(string projectRoot) =>
        File.Exists(Path.Combine(Path.GetFullPath(projectRoot), ToolsManifestRelPath.Replace('/', Path.DirectorySeparatorChar)));

    internal static string? FindHostToolsManifest()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            DirectoryInfo? dir;
            try
            {
                dir = new DirectoryInfo(Path.GetFullPath(start));
            }
            catch
            {
                continue;
            }
            for (var i = 0; dir is not null && i < 8; i++, dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, ".config", "dotnet-tools.json");
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
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

    public static async Task<DocsServeHandle> ServeAsync(
        string projectRoot,
        int port = 8080,
        int readyTimeoutMs = 180_000,
        CancellationToken ct = default)
    {
        var json = FindDocfxJson(projectRoot)
            ?? throw new InvalidOperationException("找不到 docfx.json。請先「建立／補齊體系」。");
        if (!CliUtil.CommandExists("dotnet"))
            throw new InvalidOperationException("找不到 dotnet。請安裝 .NET SDK。");
        EnsureToolsManifest(projectRoot);
        await RestoreDocfxAsync(projectRoot).ConfigureAwait(false);
        var chosen = FindFreePort(port);
        var cwd = Path.GetDirectoryName(json) ?? projectRoot;
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("docfx");
        psi.ArgumentList.Add(json);
        psi.ArgumentList.Add("--serve");
        psi.ArgumentList.Add("--hostname");
        psi.ArgumentList.Add("127.0.0.1");
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(chosen.ToString());
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var handle = new DocsServeHandle(proc, $"http://127.0.0.1:{chosen}/");
        try
        {
            if (!proc.Start())
                throw new InvalidOperationException("無法啟動 DocFX。");
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            handle.Dispose();
            throw new InvalidOperationException("無法啟動 DocFX：" + ex.Message);
        }

        try
        {
            await WaitUntilReadyAsync(handle, readyTimeoutMs, ct).ConfigureAwait(false);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public static string PreviewUrl(string projectRoot, string? docsRelPath, string? baseUrl = null)
    {
        var root = (string.IsNullOrWhiteSpace(baseUrl) ? DefaultServeUrl : baseUrl.Trim()).TrimEnd('/') + "/";
        if (string.IsNullOrWhiteSpace(docsRelPath))
            return root;
        var rel = NormalizeRel(docsRelPath);
        if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return root;
        var dest = ContentDestPrefix(projectRoot);
        var stem = rel[..^3];
        if (string.Equals(stem, "index", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(dest))
            return root;
        var path = string.IsNullOrEmpty(dest) ? stem : dest.Trim('/') + "/" + stem;
        return root + path + ".html";
    }

    public static string ContentDestPrefix(string projectRoot)
    {
        var jsonPath = FindDocfxJson(projectRoot);
        if (jsonPath is null)
            return "";
        var docsDir = FindDocsDirectory(projectRoot);
        if (docsDir is not null && IsUnder(jsonPath, docsDir))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("build", out var build)
                && build.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    var src = item.TryGetProperty("src", out var s) ? s.GetString() ?? "" : "";
                    if (!string.Equals(src.Replace('\\', '/').Trim('/'), FolderName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (item.TryGetProperty("dest", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
                        return d.GetString()!.Replace('\\', '/').Trim('/');
                    return FolderName;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // fall through
        }
        return FolderName;
    }

    public static IReadOnlyList<DocsTreeNode> BuildTree(IEnumerable<DocsFile> files)
    {
        var root = new MutableNode("", "");
        foreach (var file in files)
        {
            var rel = NormalizeRel(file.RelPath);
            if (string.IsNullOrEmpty(rel))
                continue;
            var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            var path = "";
            for (var i = 0; i < parts.Length - 1; i++)
            {
                path = string.IsNullOrEmpty(path) ? parts[i] : path + "/" + parts[i];
                node = node.Folder(parts[i], path);
            }
            node.Files.Add(file);
        }
        return root.ToNodes();
    }

    public static IReadOnlyList<DocsTreeRow> FlattenTree(IEnumerable<DocsTreeNode> nodes, ISet<string>? collapsed = null)
    {
        var rows = new List<DocsTreeRow>();
        Walk(nodes, 0);
        return rows;

        void Walk(IEnumerable<DocsTreeNode> list, int depth)
        {
            foreach (var n in list)
            {
                rows.Add(new DocsTreeRow(depth, n.Name, n.RelPath, n.IsFolder, n.File, n.Children.Count));
                if (n.IsFolder && (collapsed is null || !collapsed.Contains(n.RelPath)))
                    Walk(n.Children, depth + 1);
            }
        }
    }

    public static string NewPageStub(string relPath)
    {
        var title = TitleOf(relPath, null);
        return $"---\ntitle: {title}\n---\n\n# {title}\n\n（待補）寫這頁的說明。\n";
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

    public static async Task<string> InstallDocfxAsync(string projectRoot)
    {
        if (!CliUtil.CommandExists("dotnet"))
            throw new InvalidOperationException("需要 dotnet。請先安裝 .NET SDK：https://dot.net/");
        var created = EnsureToolsManifest(projectRoot);
        await RestoreDocfxAsync(projectRoot).ConfigureAwait(false);
        return created
            ? "已寫入 .config/dotnet-tools.json，並完成 dotnet tool restore。"
            : "已執行 dotnet tool restore。";
    }

    static async Task RestoreDocfxAsync(string projectRoot)
    {
        var manifest = Path.Combine(Path.GetFullPath(projectRoot), ToolsManifestRelPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifest))
            return;
        var (code, output) = await CliUtil.RunAsync("dotnet", ["tool", "restore"], projectRoot, 120_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(
                string.IsNullOrEmpty(output)
                    ? "dotnet tool restore 失敗，無法安裝 DocFX。"
                    : "dotnet tool restore 失敗：\n" + output);
    }

    static async Task WaitUntilReadyAsync(DocsServeHandle handle, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(timeoutMs, 3_000));
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!handle.IsRunning)
                throw new InvalidOperationException(ServeFailMessage(handle.Output));
            if (await IsHttpUpAsync(handle.Url, ct).ConfigureAwait(false))
                return;
            await Task.Delay(400, ct).ConfigureAwait(false);
        }
        if (!handle.IsRunning)
            throw new InvalidOperationException(ServeFailMessage(handle.Output));
        if (await IsHttpUpAsync(handle.Url, ct).ConfigureAwait(false))
            return;
        throw new InvalidOperationException(
            "DocFX 預覽逾時，網站還沒回應。第一次建置 API 文件可能較久，請稍後再按「網站預覽」。"
            + (string.IsNullOrWhiteSpace(handle.Output) ? "" : "\n\n" + TrimOutput(handle.Output)));
    }

    static string ServeFailMessage(string output)
    {
        var detail = TrimOutput(output);
        if (string.IsNullOrEmpty(detail))
            return "DocFX 預覽沒有起來。請確認已安裝 .NET SDK，並先建立文件體系。";
        if (detail.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("正在使用", StringComparison.Ordinal)
            || detail.Contains("只允許使用一次", StringComparison.Ordinal))
            return "預覽埠已被占用。關閉其他 DocFX／本機網站後再試。\n\n" + detail;
        return "DocFX 預覽沒有起來。\n\n" + detail;
    }

    static string TrimOutput(string output)
    {
        var text = (output ?? "").Trim();
        if (text.Length <= 1600)
            return text;
        return text[^1600..].Trim();
    }

    static async Task<bool> IsHttpUpAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static int FindFreePort(int start)
    {
        for (var port = Math.Max(start, 1); port < start + 20; port++)
        {
            if (IsPortFree(port))
                return port;
        }
        throw new InvalidOperationException($"找不到可用的預覽埠（{start}–{start + 19}）。請關掉占用的本機網站後再試。");
    }

    static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    sealed class MutableNode
    {
        public MutableNode(string name, string relPath)
        {
            Name = name;
            RelPath = relPath;
        }

        public string Name { get; }
        public string RelPath { get; }
        public Dictionary<string, MutableNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DocsFile> Files { get; } = [];

        public MutableNode Folder(string name, string relPath)
        {
            if (!Folders.TryGetValue(name, out var child))
            {
                child = new MutableNode(name, relPath);
                Folders[name] = child;
            }
            return child;
        }

        public List<DocsTreeNode> ToNodes()
        {
            var nodes = new List<DocsTreeNode>();
            foreach (var folder in Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                nodes.Add(new DocsTreeNode(folder.Name, folder.RelPath, true, null, folder.ToNodes()));
            foreach (var file in Files
                .OrderBy(f => f.IsConfig ? 1 : 0)
                .ThenBy(f => Path.GetFileName(f.RelPath), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(file.RelPath.Replace('\\', '/'));
                nodes.Add(new DocsTreeNode(name, file.RelPath, false, file, []));
            }
            return nodes;
        }
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
            "product/intake.md" => Page(
                "進件",
                "進件",
                Intro(ctx, "寫怎麼把問題開成 GitHub Issue：標題、說明、可選指派與截圖。進件表在 docs/product/intake.json。")),
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
            - name: 進件
              href: product/intake.md
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

    static void TryPruneEmptyParents(string? startDir, string docsRoot)
    {
        if (string.IsNullOrEmpty(startDir))
            return;
        var root = Path.GetFullPath(docsRoot);
        var dir = Path.GetFullPath(startDir);
        while (IsUnder(dir, root) && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(dir))
                break;
            try
            {
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    break;
                Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                break;
            }
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent))
                break;
            dir = parent;
        }
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
