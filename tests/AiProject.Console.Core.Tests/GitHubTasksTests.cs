using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class GitHubTasksTests
{
    [Fact]
    public void LooksGithubHosted_FromSlugOrUrl()
    {
        Assert.True(new GithubConfig { Owner = "acme", Repo = "app" }.LooksGithubHosted());
        Assert.True(new GithubConfig { Url = "https://github.com/acme/app.git" }.LooksGithubHosted());
        Assert.True(new GithubConfig { Url = "git@github.com:acme/app.git" }.LooksGithubHosted());
        Assert.False(new GithubConfig().LooksGithubHosted());
        Assert.False(new GithubConfig { Url = "https://gitlab.com/acme/app.git" }.LooksGithubHosted());
    }

    [Fact]
    public void FindIssueNumbers_FromPrBody()
    {
        var found = GitHubLifecycle.FindIssueNumbers("Closes #12 and #15");
        Assert.Equal([12, 15], found);
    }

    [Theory]
    [InlineData("sjvann", "sjvann")]
    [InlineData("\"octocat\"", "octocat")]
    [InlineData("{\"login\":\"octocat\",\"id\":1}", "octocat")]
    [InlineData("", null)]
    [InlineData("not a user!!", null)]
    public void ParseUserLogin_AcceptsJqAndJson(string text, string? expected)
    {
        Assert.Equal(expected, GitHubAuth.ParseUserLogin(text));
    }

    [Theory]
    [InlineData("! First copy your one-time code: ABCD-EFGH", "ABCD-EFGH")]
    [InlineData("! One-time code (A12B-C34D) copied to clipboard", "A12B-C34D")]
    [InlineData("! First copy your one-time code: \u001b[1mWXYZ-9876\u001b[0m", "WXYZ-9876")]
    [InlineData("no code here", null)]
    [InlineData("", null)]
    public void ParseDeviceCode_FromGhOutput(string text, string? expected)
    {
        Assert.Equal(expected, GitHubAuth.ParseDeviceCode(text));
    }

    [Theory]
    [InlineData(
        "Open this URL to continue in your web browser: https://github.com/login/device",
        "https://github.com/login/device")]
    [InlineData(
        "Press Enter to open https://github.com/login/device in your browser...",
        "https://github.com/login/device")]
    [InlineData(
        "Open this URL to continue in your web browser: https://ghe.corp.com/login/device",
        "https://ghe.corp.com/login/device")]
    [InlineData("random https://example.com/not-auth", null)]
    [InlineData("", null)]
    public void ParseBrowserUrl_FromGhOutput(string text, string? expected)
    {
        Assert.Equal(expected, GitHubAuth.ParseBrowserUrl(text));
    }

    [Fact]
    public void ParseIssues_SplitsMineAndUnassigned()
    {
        const string json = """
            [
              {
                "number": 12,
                "title": "修好啟動",
                "state": "OPEN",
                "url": "https://github.com/acme/app/issues/12",
                "body": "服務起不來",
                "updatedAt": "2026-08-26T02:00:00Z",
                "labels": [{"name": "bug"}],
                "assignees": [{"login": "sjvann"}]
              },
              {
                "number": 15,
                "title": "補文件",
                "state": "OPEN",
                "url": "https://github.com/acme/app/issues/15",
                "body": "",
                "updatedAt": "2026-08-25T10:00:00Z",
                "labels": [],
                "assignees": []
              },
              {
                "number": 9,
                "title": "別人的",
                "state": "OPEN",
                "url": "https://github.com/acme/app/issues/9",
                "body": "",
                "updatedAt": "2026-08-24T10:00:00Z",
                "labels": [],
                "assignees": [{"login": "other"}]
              }
            ]
            """;
        var issues = GitHubIssues.ParseIssues(json);
        Assert.Equal(3, issues.Count);
        Assert.Equal("#12", issues[0].NumberText);
        Assert.True(issues[0].AssignedTo("sjvann"));
        Assert.Equal("bug", issues[0].LabelText);
        Assert.False(issues[1].IsAssigned);

        var (mine, open) = GitHubIssues.Split(issues, "sjvann");
        Assert.Single(mine);
        Assert.Equal(12, mine[0].Number);
        Assert.Single(open);
        Assert.Equal(15, open[0].Number);
    }

    [Fact]
    public void ParseIssues_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(GitHubIssues.ParseIssues(""));
        Assert.Empty(GitHubIssues.ParseIssues("not-json"));
        Assert.Empty(GitHubIssues.ParseIssues("{}"));
    }

    [Fact]
    public void BuildIssueAgentPrompt_IncludesNumberAndBody()
    {
        var issue = new GithubIssue(
            12,
            "修好啟動",
            "OPEN",
            "https://github.com/acme/app/issues/12",
            "服務起不來",
            "2026-08-26T02:00:00Z",
            ["bug"],
            ["sjvann"]);
        var prompt = CursorLauncher.BuildIssueAgentPrompt(@"C:\proj", issue);
        Assert.Contains("GitHub Issue", prompt);
        Assert.Contains("#12", prompt);
        Assert.Contains("修好啟動", prompt);
        Assert.Contains("服務起不來", prompt);
        Assert.Contains("C:\\proj", prompt);
        Assert.Contains("stack_status", prompt);
    }

    [Fact]
    public void ParseView_ReadsIssueAndComments()
    {
        const string json = """
            {
              "number": 3,
              "title": "關於我的任務",
              "state": "OPEN",
              "url": "https://github.com/acme/app/issues/3",
              "body": "點選任務時，會回到 Github issue 去",
              "updatedAt": "2026-09-05T10:00:00Z",
              "labels": [{"name": "enhancement"}],
              "assignees": [{"login": "sjvann"}],
              "comments": [
                {
                  "author": {"login": "sjvann"},
                  "body": "先在控制台回應",
                  "createdAt": "2026-09-05T11:00:00Z",
                  "url": "https://github.com/acme/app/issues/3#issuecomment-1"
                },
                {
                  "author": "octocat",
                  "body": "再用 PR",
                  "createdAt": "2026-09-05T12:00:00Z"
                }
              ]
            }
            """;
        var (issue, comments) = GitHubIssues.ParseView(json);
        Assert.NotNull(issue);
        Assert.Equal(3, issue.Number);
        Assert.Equal("關於我的任務", issue.Title);
        Assert.Equal("enhancement", issue.LabelText);
        Assert.Equal(2, comments.Count);
        Assert.Equal("sjvann", comments[0].Author);
        Assert.Equal("先在控制台回應", comments[0].Body);
        Assert.Equal("octocat", comments[1].Author);
        Assert.Equal("再用 PR", comments[1].Body);
    }

    [Fact]
    public void ParseView_EmptyOrInvalid_ReturnsNone()
    {
        Assert.Null(GitHubIssues.ParseView("").Issue);
        Assert.Empty(GitHubIssues.ParseView("").Comments);
        Assert.Null(GitHubIssues.ParseView("not-json").Issue);
        Assert.Null(GitHubIssues.ParseView("[]").Issue);
    }

    [Fact]
    public void PullRequestClosesBody_WritesKeyword()
    {
        Assert.Equal("Closes #3", GitHubService.PullRequestClosesBody(3));
        Assert.Equal("", GitHubService.PullRequestClosesBody(0));
    }

    [Fact]
    public void BuildCreatePrArgs_AddsClosesBody()
    {
        var cfg = new GithubConfig { Owner = "acme", Repo = "app", DefaultBranch = "main" };
        var args = GitHubService.BuildCreatePrArgs(cfg, 4);
        Assert.Equal(["pr", "create", "--base", "main", "--fill", "--body", "Closes #4"], args);
    }
}
