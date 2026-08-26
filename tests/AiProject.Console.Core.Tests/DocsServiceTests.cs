using System.Text.Json;
using AiProject.Console.Core;
using AiProject.Console.Core.Actions;
using AiProject.Console.Core.Docs;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Stack;

namespace AiProject.Console.Core.Tests;

public class DocsServiceTests
{
    [Fact]
    public void IsSafeRelPath_RejectsTraversalAndOddExt()
    {
        Assert.True(DocsService.IsSafeRelPath("user/getting-started.md"));
        Assert.True(DocsService.IsSafeRelPath("toc.yml"));
        Assert.True(DocsService.IsSafeRelPath("docfx.json"));
        Assert.False(DocsService.IsSafeRelPath("../secret.md"));
        Assert.False(DocsService.IsSafeRelPath("user/../../x.md"));
        Assert.False(DocsService.IsSafeRelPath("notes.txt"));
        Assert.False(DocsService.IsSafeRelPath(""));
        Assert.False(DocsService.IsSafeRelPath("/abs.md"));
    }

    [Fact]
    public void ResolveInsideDocs_StaysUnderDocs()
    {
        var root = NewTemp();
        try
        {
            Assert.Throws<InvalidOperationException>(() => DocsService.ResolveInsideDocs(root, "../outside.md"));
            var full = DocsService.ResolveInsideDocs(root, "user/getting-started.md");
            Assert.Contains($"{Path.DirectorySeparatorChar}docs{Path.DirectorySeparatorChar}", full);
            Assert.EndsWith("getting-started.md", full, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Scan_MissingWithoutDocsFolder()
    {
        var root = NewTemp();
        try
        {
            var status = DocsService.Scan(root);
            Assert.Equal(DocsHealth.Missing, status.Health);
            Assert.Equal("無文件", status.Label());
            Assert.Contains("README.md", status.MissingScaffold);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Scaffold_CreatesThenDoesNotOverwrite()
    {
        var root = NewTemp();
        try
        {
            var ctx = new DocsScaffoldContext("示範", root, ["api (api)"], ["Demo.Api"], "# 示範產品", "acme/demo");
            var first = DocsService.Scaffold(root, ctx);
            Assert.Contains("README.md", first.Created);
            Assert.Contains("docfx.json", first.Created);
            Assert.Contains(DocsService.WorkflowRelPath, first.Created);
            Assert.Contains(DocsService.ToolsManifestRelPath, first.Created);

            var readme = Path.Combine(root, "docs", "README.md");
            File.WriteAllText(readme, "---\ntitle: 已改\n---\n\n# 已改\n");
            var second = DocsService.Scaffold(root, ctx);
            Assert.DoesNotContain("README.md", second.Created);
            Assert.Contains("README.md", second.Skipped);
            Assert.Contains("已改", File.ReadAllText(readme));

            var status = DocsService.Scan(root);
            Assert.Equal(DocsHealth.Draft, status.Health);
            Assert.True(status.HasToc);
            Assert.True(status.HasDocfx);
            Assert.True(status.HasWorkflow);
            Assert.True(status.StubCount > 0);
            Assert.Contains(status.Files, f => f.RelPath == "user/getting-started.md" && f.IsStub);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void WriteAndRead_RoundTrip()
    {
        var root = NewTemp();
        try
        {
            DocsService.Write(root, "user/note.md", "---\ntitle: 筆記\n---\n\n# 筆記\n");
            Assert.Contains("筆記", DocsService.Read(root, "user/note.md"));
            var status = DocsService.Scan(root);
            Assert.Equal(DocsHealth.Incomplete, status.Health);
            Assert.Contains(status.Files, f => f.Title == "筆記");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Ready_WhenSkeletonHasNoTodo()
    {
        var root = NewTemp();
        try
        {
            DocsService.Scaffold(root, new DocsScaffoldContext("X", root, [], [], "", null));
            foreach (var rel in DocsService.ScaffoldFiles)
            {
                if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    continue;
                var text = DocsService.Read(root, rel).Replace("待補", "已寫");
                DocsService.Write(root, rel, text);
            }
            var status = DocsService.Scan(root);
            Assert.Equal(DocsHealth.Ready, status.Health);
            Assert.Equal(0, status.StubCount);
            Assert.True(status.IsOk);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void WorkflowAndDocfxTemplates_PointAtDocsFolder()
    {
        var yml = DocsService.WorkflowTemplate();
        Assert.Contains("dotnet docfx docs/docfx.json", yml);
        Assert.Contains("path: docs/_site", yml);
        Assert.Contains("dotnet tool restore", yml);
        Assert.Contains(DocsService.DocfxToolVersion, DocsService.ToolsManifestTemplate());

        var root = NewTemp();
        try
        {
            DocsService.Scaffold(root, new DocsScaffoldContext("站", root, [], [], "", null));
            var json = DocsService.Read(root, "docfx.json");
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("_site", doc.RootElement.GetProperty("build").GetProperty("output").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PagesUrl_FromGithubConfig()
    {
        Assert.Equal("https://acme.github.io/demo/", DocsService.PagesUrl(new GithubConfig { Owner = "Acme", Repo = "demo" }));
        Assert.Equal("", DocsService.PagesUrl(new GithubConfig()));
    }

    [Fact]
    public void Prompts_IncludeStubsAndContext()
    {
        var root = NewTemp();
        try
        {
            var ctx = new DocsScaffoldContext("示範", root, ["web"], ["Web"], "README 摘錄", "acme/demo");
            DocsService.Scaffold(root, ctx);
            var status = DocsService.Scan(root);
            var all = DocsPrompts.FillAll(status, ctx);
            Assert.Contains("docs/", all);
            Assert.Contains("user/getting-started.md", all);
            Assert.Contains("待補", all);
            Assert.Contains("acme/demo", all);
            var one = DocsPrompts.FillOne("user/daily-use.md", DocsService.Read(root, "user/daily-use.md"), ctx);
            Assert.Contains("user/daily-use.md", one);
            Assert.Contains("日常操作", one);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ActionCatalog_IncludesDocsGroup()
    {
        var actions = ActionCatalog.Load("docs");
        Assert.Contains(actions, a => a.Id == "docs_scaffold" && a.Handler == "docs_scaffold");
        Assert.Contains(actions, a => a.Id == "docs_open_folder");
        Assert.Contains(actions, a => a.Id == "docs_ai_fill");
        Assert.Contains(actions, a => a.Id == "docs_serve");
        Assert.Contains(actions, a => a.Id == "docs_enable_pages" && a.RequiresGithub);
        Assert.Contains(actions, a => a.Id == "docs_publish_pages" && a.RequiresGithub);
    }

    [Fact]
    public async Task Mcp_ReadOnlyDocsTools()
    {
        var root = NewTemp();
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "# T");
            DocsService.Scaffold(root, new DocsScaffoldContext("T", root, [], [], "# T", null));
            var ws = StackWorkspace.Open(root);
            var status = await StackToolRouter.InvokeAsync(ws, "docs_status");
            Assert.Contains("draft", status, StringComparison.OrdinalIgnoreCase);
            var list = await StackToolRouter.InvokeAsync(ws, "list_docs");
            Assert.Contains("getting-started.md", list);
            var read = await StackToolRouter.InvokeAsync(ws, "read_doc", new Dictionary<string, string?>
            {
                ["path"] = "user/getting-started.md",
            });
            Assert.Contains("待補", read);
            var denied = await StackToolRouter.InvokeAsync(ws, "read_doc", new Dictionary<string, string?>
            {
                ["path"] = "../x.md",
            });
            Assert.Contains("相對路徑", denied);
            Assert.Contains("docs_status", McpPolicy.ReadOnlyTools);
            Assert.Contains("list_docs", McpPolicy.ReadOnlyTools);
            Assert.Contains("read_doc", McpPolicy.ReadOnlyTools);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void DoctorLine_MentionsDocs()
    {
        var line = DocsService.DoctorLine(null);
        Assert.Contains("文件", line);
        Assert.Contains("dotnet", line);
    }

    [Fact]
    public void TitleOf_ReadsFrontMatter()
    {
        Assert.Equal("安裝", DocsService.TitleOf("user/x.md", "---\ntitle: 安裝\n---\n\n# 別的\n"));
        Assert.Equal("標題", DocsService.TitleOf("a.md", "# 標題\n"));
        Assert.Equal("a", DocsService.TitleOf("user/a.md", ""));
    }

    static string NewTemp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // temp leftovers
        }
    }
}
