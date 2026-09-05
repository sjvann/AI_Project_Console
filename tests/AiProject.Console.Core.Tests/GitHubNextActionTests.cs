using AiProject.Console.Core;
using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class GitHubNextActionTests
{
    [Fact]
    public void DirtyWorkspace_CommitsFirst()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("feat", 3, 2, 1), hasPr: false);
        Assert.Equal(GitHubNextAction.KindCommit, step.Kind);
        Assert.Equal("github_commit", step.Handler);
        Assert.Equal(GithubActionLanes.Daily, step.Lane);
        Assert.Contains("3 筆未提交", step.Hint);
        Assert.False(step.OpensHub);
    }

    [Fact]
    public void BehindRemote_SyncsBeforePublish()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("main", 0, 0, 2), hasPr: true);
        Assert.Equal(GitHubNextAction.KindSync, step.Kind);
        Assert.Equal("github_sync", step.Handler);
        Assert.Contains("落後", step.Hint);
    }

    [Fact]
    public void NoUpstream_Publishes()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("feat", 0, null, null, false), hasPr: false);
        Assert.Equal(GitHubNextAction.KindPublish, step.Kind);
        Assert.Equal("github_publish", step.Handler);
        Assert.Contains("遠端追蹤", step.Hint);
    }

    [Fact]
    public void AheadOfRemote_Publishes()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("main", 0, 2, 0), hasPr: true);
        Assert.Equal(GitHubNextAction.KindPublish, step.Kind);
        Assert.Contains("尚未發布", step.Hint);
    }

    [Fact]
    public void FeatureBranchWithoutPr_OpensPr()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("feat/login", 0, 0, 0), hasPr: false, defaultBranch: "main");
        Assert.Equal(GitHubNextAction.KindPr, step.Kind);
        Assert.Equal("github_pr", step.Handler);
        Assert.Equal(GithubActionLanes.Collab, step.Lane);
    }

    [Fact]
    public void FeatureBranchWithPr_DoesNotAskAgain()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("feat/login", 0, 0, 0), hasPr: true);
        Assert.Equal(GitHubNextAction.KindHub, step.Kind);
        Assert.True(step.OpensHub);
    }

    [Fact]
    public void CiInProgress_OpensActions()
    {
        var step = GitHubNextAction.Decide(
            new GitBriefStatus("main", 0, 0, 0),
            hasPr: true,
            ciInProgress: true);
        Assert.Equal(GitHubNextAction.KindActions, step.Kind);
        Assert.Equal("github_actions", step.Handler);
        Assert.Equal(GithubActionLanes.Ship, step.Lane);
    }

    [Fact]
    public void CiJustFailed_OpensActions()
    {
        var step = GitHubNextAction.Decide(
            new GitBriefStatus("main", 0, 0, 0),
            hasPr: true,
            ciJustFailed: true);
        Assert.Equal(GitHubNextAction.KindActions, step.Kind);
        Assert.Contains("失敗", step.Hint);
    }

    [Fact]
    public void HistoricalCiFailure_DoesNotOverrideClean()
    {
        var step = GitHubNextAction.Decide(new GitBriefStatus("main", 0, 0, 0), hasPr: true);
        Assert.Equal(GitHubNextAction.KindHub, step.Kind);
        Assert.Equal("已與遠端一致。", step.Hint);
        Assert.True(step.OpensHub);
    }

    [Fact]
    public void MissingBrief_OpensHub()
    {
        var step = GitHubNextAction.Decide(null, hasPr: false);
        Assert.True(step.OpensHub);
        Assert.Contains("尚未讀到", step.Hint);
    }

    [Fact]
    public void DefaultBranchMatch_IsCaseInsensitive()
    {
        Assert.True(GitHubNextAction.IsDefaultBranch("Main", "main"));
        Assert.True(GitHubNextAction.IsDefaultBranch("HEAD", "develop"));
        Assert.False(GitHubNextAction.IsDefaultBranch("feat", "main"));
    }

    [Fact]
    public void CreatePr_OnDefaultBranch_ExplainsInsteadOfCallingGh()
    {
        var reason = IssueCompletion.CreatePrBlockReason(
            new GitBriefStatus("main", 0, 0, 0), "main", hasPr: false);
        Assert.Contains("預設分支", reason);
        Assert.True(IssueCompletion.NeedsTaskBranch(new GitBriefStatus("main", 2, 0, 0), "main"));
        Assert.False(IssueCompletion.NeedsTaskBranch(new GitBriefStatus("issue-4", 0, 0, 0), "main"));
        Assert.Null(IssueCompletion.CreatePrBlockReason(
            new GitBriefStatus("feat/login", 0, 0, 0), "main", hasPr: false));
    }

    [Fact]
    public void CreatePr_DirtyFeatureBranch_TellsCommentIsNotCommit()
    {
        var reason = IssueCompletion.CreatePrBlockReason(
            new GitBriefStatus("issue-4", 3, 0, 0), "main", hasPr: false);
        Assert.Contains("送出回應", reason);
        Assert.Contains("未提交", reason);
    }

    [Fact]
    public void CreatePr_UnpublishedBranch_AsksToPublishFirst()
    {
        var reason = IssueCompletion.CreatePrBlockReason(
            new GitBriefStatus("issue-4", 0, null, null, false), "main", hasPr: false);
        Assert.Contains("尚未發布", reason);
    }

    [Fact]
    public void ExplainCreatePrFailure_TranslatesNoCommits()
    {
        var msg = IssueCompletion.ExplainCreatePrFailure(
            "could not compute title or body defaults: could not find any commits between origin/main and HEAD");
        Assert.Contains("沒有新提交", msg);
        Assert.True(IssueCompletion.LooksLikeNoCommits("No commits between main and main"));
    }

    [Fact]
    public void SuggestIssueBranchName_SlugsAsciiAndFallsBack()
    {
        Assert.Equal("issue-4", IssueCompletion.SuggestIssueBranchName(4, "完成issue的過程"));
        Assert.Equal("issue-12-fix-login", IssueCompletion.SuggestIssueBranchName(12, "Fix login"));
        Assert.Equal("issue-3", IssueCompletion.SuggestIssueBranchName(3, ""));
    }

    [Fact]
    public void AccountStatus_RedWhenLoggedOut_YellowWhenDirty_GreenWhenClean()
    {
        Assert.Equal(GithubAccountStatus.Danger, GithubAccountStatus.Tone(loggedIn: false, needsAttention: false));
        Assert.Equal(GithubAccountStatus.Wait, GithubAccountStatus.Tone(loggedIn: false, needsAttention: true));
        Assert.Equal(GithubAccountStatus.Wait, GithubAccountStatus.Tone(loggedIn: true, needsAttention: true));
        Assert.Equal(GithubAccountStatus.Ok, GithubAccountStatus.Tone(loggedIn: true, needsAttention: false));
        Assert.Contains("3 筆未提交", GithubAccountStatus.Title(true, new GitBriefStatus("feat", 3, 0, 0)));
        Assert.Equal("已登入 GitHub", GithubAccountStatus.Title(true, new GitBriefStatus("main", 0, 0, 0)));
        Assert.Equal("尚未登入 GitHub", GithubAccountStatus.Title(false, null));
        Assert.Contains("落後遠端 2", GithubAccountStatus.Title(true, new GitBriefStatus("main", 0, 0, 2)));
    }
}
