using AiProject.Console.Core;
using AiProject.Console.Core.Actions;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tests;

public class DailyWorkflowTests
{
    [Fact]
    public void GitBriefStatus_Format_ShowsDirtyAndAheadBehind()
    {
        Assert.Equal("main · 乾淨", new GitBriefStatus("main", 0, 0, 0).Format());
        Assert.Equal("feat · 3 未提交 · ↑2 · ↓1", new GitBriefStatus("feat", 3, 2, 1).Format());
        Assert.Equal("dev · 乾淨", new GitBriefStatus("dev", 0, null, null).Format());
        Assert.Equal("main · ↑1", new GitBriefStatus("main", 0, 1, 0).Format());
    }

    [Fact]
    public void ActionCatalog_IncludesLocalCommit()
    {
        var commit = ActionCatalog.Load("github").Single(a => a.Id == "github_commit");
        Assert.Equal("提交…", commit.Label);
        Assert.Equal("github_commit", commit.Handler);
        Assert.False(commit.RequiresGithub);
    }

    [Fact]
    public void ParsePorcelain_KeepsUnstagedAndRenames()
    {
        var changes = GitHubService.ParsePorcelain("""
             M src/a.cs
            M  src/b.cs
            ?? new.txt
            R  old.txt -> new-name.txt
            """);
        Assert.Equal(4, changes.Count);
        Assert.Equal("修改  src/a.cs", changes[0].Display());
        Assert.Equal("修改  src/b.cs", changes[1].Display());
        Assert.Equal("未追蹤  new.txt", changes[2].Display());
        Assert.Equal("重新命名  old.txt → new-name.txt", changes[3].Display());
        Assert.Equal(" M", changes[0].Code);
        Assert.Equal("src/a.cs", changes[0].Path);
    }

    [Fact]
    public void ParsePorcelain_TrimmedStdoutShiftsUnstagedFirstPath()
    {
        const string porcelain = " M alpha.txt\n?? extra.txt";
        var intact = GitHubService.ParsePorcelain(porcelain);
        Assert.Equal("alpha.txt", intact[0].Path);
        Assert.Equal(" M", intact[0].Code);

        var shifted = GitHubService.ParsePorcelain(porcelain.Trim());
        Assert.Equal("lpha.txt", shifted[0].Path);
    }

    [Fact]
    public async Task ListChangesAsync_KeepsUnstagedFirstPath()
    {
        if (!CliUtil.CommandExists("git"))
            return;
        var root = Path.Combine(Path.GetTempPath(), "ai-console-porcelain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, (await CliUtil.RunAsync("git", ["init"], root)).Code);
            await CliUtil.RunAsync("git", ["config", "user.email", "test@example.com"], root);
            await CliUtil.RunAsync("git", ["config", "user.name", "Test"], root);
            File.WriteAllText(Path.Combine(root, "alpha.txt"), "one");
            Assert.Contains("已提交", await GitHubService.CommitAsync(root, "seed"));
            File.WriteAllText(Path.Combine(root, "alpha.txt"), "two");

            var changes = await GitHubService.ListChangesAsync(root);
            var only = Assert.Single(changes);
            Assert.Equal("alpha.txt", only.Path);
            Assert.Equal(" M", only.Code);
            Assert.Equal("修改  alpha.txt", only.Display());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void UnescapeGitPath_DecodesQuotedOctalUtf8()
    {
        var path = CommitMessageSuggester.UnescapeGitPath(
            "\"Requirement/FHIR Profile Server \\346\\226\\207\\344\\273\\266.md\"");
        Assert.Equal("Requirement/FHIR Profile Server 文件.md", path);
    }

    [Fact]
    public void ParsePorcelain_UnescapesQuotedPaths()
    {
        var changes = GitHubService.ParsePorcelain(
            "?? \"Requirement/QA-\\346\\270\\254\\350\\251\\246.md\"");
        Assert.Single(changes);
        Assert.Equal("Requirement/QA-測試.md", changes[0].Path);
        Assert.Equal("未追蹤  Requirement/QA-測試.md", changes[0].Display());
    }

    [Fact]
    public void DraftFromContext_SummarizesDocsAndConfig()
    {
        var ctx = new CommitContext(
            [
                new GitChange("??", "Requirement/FHIR Profile Server 文件.md"),
                new GitChange("??", "Requirement/QA-測試.md"),
                new GitChange(" M", ".gitignore"),
                new GitChange(" M", "ai-project.json"),
            ],
            "",
            "",
            []);
        var draft = CommitMessageSuggester.DraftFromContext(ctx);
        Assert.Contains("文件", draft);
        Assert.Contains("設定", draft);
        Assert.Contains("ai-project.json", draft);
    }

    [Fact]
    public void CleanMessage_StripsFencesAndPrefix()
    {
        var text = CommitMessageSuggester.CleanMessage("```\n提交說明：更新需求文件\n```");
        Assert.Equal("更新需求文件", text);
    }

    [Fact]
    public async Task CommitAsync_StagesAndCommitsDirtyFiles()
    {
        if (!CliUtil.CommandExists("git"))
            return;
        var root = Path.Combine(Path.GetTempPath(), "ai-console-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, (await CliUtil.RunAsync("git", ["init"], root)).Code);
            await CliUtil.RunAsync("git", ["config", "user.email", "test@example.com"], root);
            await CliUtil.RunAsync("git", ["config", "user.name", "Test"], root);
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var result = await GitHubService.CommitAsync(root, "Add a.txt");
            Assert.Contains("已提交", result);
            Assert.Equal(0, await GitHubService.DirtyCountAsync(root));
            await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubService.CommitAsync(root, "   "));
            await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubService.CommitAsync(root, "nothing left"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    static void TryDeleteDirectory(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); }
            catch { /* ignore */ }
        }
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Windows 可能短暫鎖住 .git
        }
    }

    [Fact]
    public void TextFilter_Apply_FiltersLinesCaseInsensitive()
    {
        var text = "INFO start\nERROR port 5101\ninfo ready";
        Assert.Equal(text, TextFilter.Apply(text, ""));
        Assert.Equal(text, TextFilter.Apply(text, "   "));
        Assert.Equal("ERROR port 5101", TextFilter.Apply(text, "error"));
        Assert.Equal("INFO start\ninfo ready", TextFilter.Apply(text, "info"));
        Assert.Equal("", TextFilter.Apply(text, "missing"));
    }
}
