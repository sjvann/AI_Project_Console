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
        Assert.Equal("feat · 無遠端追蹤", new GitBriefStatus("feat", 0, null, null, false).Format());
    }

    [Fact]
    public void GitBriefStatus_LeaveBlockReason_RequiresCleanAndPublished()
    {
        Assert.Null(new GitBriefStatus("main", 0, 0, 0).LeaveBlockReason());
        Assert.Contains("未提交", new GitBriefStatus("main", 2, 0, 0).LeaveBlockReason());
        Assert.Contains("尚未發布", new GitBriefStatus("main", 0, 1, 0).LeaveBlockReason());
        Assert.Contains("遠端追蹤", new GitBriefStatus("feat", 0, null, null, false).LeaveBlockReason());
        Assert.False(new GitBriefStatus("main", 1, 0, 0).IsClearToLeave);
        Assert.True(new GitBriefStatus("main", 0, 0, 1).IsClearToLeave);
    }

    [Fact]
    public void ActionCatalog_IncludesLocalCommit()
    {
        var commit = ActionCatalog.Load("github").Single(a => a.Id == "github_commit");
        Assert.Equal("提交…", commit.Label);
        Assert.Equal("github_commit", commit.Handler);
        Assert.False(commit.RequiresGithub);
        var switchBranch = ActionCatalog.Load("github").Single(a => a.Id == "github_switch_branch");
        Assert.Equal("切換分支…", switchBranch.Label);
        Assert.False(switchBranch.RequiresGithub);
        var actions = ActionCatalog.Load("github").Single(a => a.Id == "github_actions");
        Assert.Equal("Actions 狀態…", actions.Label);
        Assert.True(actions.RequiresGithub);
        var scaffold = ActionCatalog.Load("github").Single(a => a.Id == "github_ci_scaffold");
        Assert.Equal("補齊 CI workflow…", scaffold.Label);
        Assert.Contains("不覆蓋", scaffold.Confirm);
        var test = ActionCatalog.Load("build").Single(a => a.Id == "build_test");
        Assert.Equal("跑測試", test.Label);
        Assert.Equal("build_test", test.Handler);
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
    public void PickAgentMessage_KeepsStdoutAndDropsStderrDiagnostics()
    {
        var message = CommitMessageSuggester.PickAgentMessage(
            "修正搜尋條件驗證\n\n- 補上必填檢查",
            "⚠ Workspace Trust Required\n\n  Pass --trust, --yolo, or -f if you trust this directory");
        Assert.Equal("修正搜尋條件驗證\n\n- 補上必填檢查", message);
        Assert.DoesNotContain("Workspace Trust", message);
        Assert.DoesNotContain("Pass --trust", message);
    }

    [Fact]
    public void PickAgentMessage_StripsDiagnosticLinesMixedIntoStdout()
    {
        var message = CommitMessageSuggester.PickAgentMessage(
            "更新提交建議\n⚠ Workspace Trust Required\nError: Authentication required. Please run 'agent login' first",
            "");
        Assert.Equal("更新提交建議", message);
    }

    [Fact]
    public void PickAgentMessage_RejectsReconnectAndCertificateLog()
    {
        var log = """
            Connection lost, reconnecting to https://agentn.global.api5.cursor.sh (attempt 1)...
            Retry attempt 1...
            Connection lost, reconnecting to https://agentn.global.api5.cursor.sh (attempt 2)...
            Retry attempt 2...
            Connection lost, reconnecting to https://agentn.global.api5.cursor.sh (attempt 3)...
            Retry attempt 3...
            RetriableError: [internal] self-signed certificate in certificate chain
            """;
        Assert.Null(CommitMessageSuggester.PickAgentMessage(log, ""));
        var hint = CommitMessageSuggester.LocalDraftHint(log);
        Assert.Contains("連線失敗", hint);
        Assert.DoesNotContain("已用 Cursor Agent 產生", hint);
    }

    [Fact]
    public void LocalDraftHint_MissingCliVsAuthFailure()
    {
        Assert.Contains("未偵測到 Cursor Agent CLI", CommitMessageSuggester.LocalDraftHint(null));
        var hint = CommitMessageSuggester.LocalDraftHint(
            "Error: Authentication required. Please run 'agent login' first, or set CURSOR_API_KEY environment variable.");
        Assert.Contains("尚未登入", hint);
        Assert.Contains("agent login", hint);
        Assert.DoesNotContain("未偵測到", hint);
    }

    [Fact]
    public void LocalDraftHint_WorkspaceTrust()
    {
        var hint = CommitMessageSuggester.LocalDraftHint(
            "⚠ Workspace Trust Required\n\n  Pass --trust, --yolo, or -f if you trust this directory");
        Assert.Contains("工作區信任", hint);
        Assert.DoesNotContain("呼叫失敗", hint);
    }

    [Fact]
    public void BuildAgentFlags_PutsTrustBeforeWorkspace()
    {
        var flags = CommitMessageSuggester.BuildAgentFlags(@"C:\proj");
        Assert.Equal("-p", flags[0]);
        Assert.Contains("--trust", flags);
        Assert.True(Array.IndexOf(flags, "--trust") < Array.IndexOf(flags, "--workspace"));
        Assert.DoesNotContain(flags, f => f.Contains("請依") || f.Contains('\n'));
    }

    [Fact]
    public void TryUnwrapWindowsAgent_FindsNodeWhenInstalled()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var cli = CommitMessageSuggester.ResolveAgentCli();
        if (cli is null)
            return;
        var unwrapped = CommitMessageSuggester.TryUnwrapWindowsAgent(cli);
        if (unwrapped is null)
            return;
        Assert.True(File.Exists(unwrapped.Value.FileName));
        Assert.EndsWith("node.exe", unwrapped.Value.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(unwrapped.Value.PrefixArgs, a => a.EndsWith("index.js", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void IsValidBranchName_RejectsUnsafe()
    {
        Assert.True(GitHubService.IsValidBranchName("feat/login"));
        Assert.True(GitHubService.IsValidBranchName("main"));
        Assert.False(GitHubService.IsValidBranchName(""));
        Assert.False(GitHubService.IsValidBranchName("HEAD"));
        Assert.False(GitHubService.IsValidBranchName("feat login"));
        Assert.False(GitHubService.IsValidBranchName("feat..x"));
        Assert.False(GitHubService.IsValidBranchName("-bad"));
        Assert.Equal("feat/login", GitBranchInfo.StripRemotePrefix("origin/feat/login"));
    }

    [Fact]
    public async Task ListAndSwitchBranch_RequiresCleanWorkspace()
    {
        if (!CliUtil.CommandExists("git"))
            return;
        var root = Path.Combine(Path.GetTempPath(), "ai-console-branch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, (await CliUtil.RunAsync("git", ["init"], root)).Code);
            await CliUtil.RunAsync("git", ["config", "user.email", "test@example.com"], root);
            await CliUtil.RunAsync("git", ["config", "user.name", "Test"], root);
            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            await GitHubService.CommitAsync(root, "seed");
            await CliUtil.RunAsync("git", ["branch", "-M", "main"], root);

            var created = await GitHubService.CreateBranchAsync(root, "feat/switch");
            Assert.Contains("feat/switch", created);
            var brief = await GitHubService.TryBriefStatusAsync(root);
            Assert.Equal("feat/switch", brief?.Branch);
            Assert.False(brief!.HasUpstream);

            var listed = await GitHubService.ListBranchesAsync(root, fetchRemote: false);
            Assert.Contains(listed, b => b.Name == "main");
            Assert.Contains(listed, b => b.Name == "feat/switch" && b.IsCurrent);

            File.WriteAllText(Path.Combine(root, "a.txt"), "dirty");
            var dirty = await Assert.ThrowsAsync<InvalidOperationException>(() => GitHubService.SwitchBranchAsync(root, "main"));
            Assert.Contains("未提交", dirty.Message);

            File.WriteAllText(Path.Combine(root, "a.txt"), "hello");
            var switched = await GitHubService.SwitchBranchAsync(root, "main");
            Assert.Contains("main", switched);
            brief = await GitHubService.TryBriefStatusAsync(root);
            Assert.Equal("main", brief?.Branch);
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
