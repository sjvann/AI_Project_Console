using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class GitHostTests
{
    [Theory]
    [InlineData("", "github.com")]
    [InlineData("github.com", "github.com")]
    [InlineData("https://ghe.corp.com/org/repo.git", "ghe.corp.com")]
    [InlineData("git@ghe.corp.com:org/repo.git", "ghe.corp.com")]
    public void Normalize_ReadsHost(string raw, string expected)
    {
        Assert.Equal(expected, GitHost.Normalize(raw));
    }

    [Fact]
    public void LooksGithubHosted_UsesKindNotOnlyPublicSite()
    {
        Assert.True(new GithubConfig { Owner = "acme", Repo = "app" }.LooksGithubHosted());
        Assert.True(new GithubConfig { Url = "https://github.com/acme/app.git" }.LooksGithubHosted());
        Assert.True(new GithubConfig { Url = "https://ghe.corp.com/acme/app.git", Host = "ghe.corp.com", Kind = GitHost.KindEnterprise }.LooksGithubHosted());
        Assert.False(new GithubConfig().LooksGithubHosted());
        Assert.False(new GithubConfig { Url = "https://gitlab.com/acme/app.git" }.LooksGithubHosted());
        Assert.False(new GithubConfig { Owner = "acme", Repo = "app", Kind = GitHost.KindGitOnly }.LooksGithubHosted());
    }

    [Fact]
    public void WebUrl_UsesConfiguredHost()
    {
        var cfg = new GithubConfig { Owner = "acme", Repo = "app", Host = "ghe.corp.com" };
        Assert.Equal("https://ghe.corp.com/acme/app", cfg.WebUrl());
        Assert.Equal("https://ghe.corp.com/acme/app.git", cfg.RemoteUrl());
    }

    [Fact]
    public void DisplayAccount_AddsHostWhenNotPublic()
    {
        Assert.Equal("@alice", GitHost.DisplayAccount("alice", "github.com"));
        Assert.Equal("@alice · ghe.corp.com", GitHost.DisplayAccount("alice", "ghe.corp.com"));
    }

    [Fact]
    public void ParseCreated_ReadsIssueUrl()
    {
        var created = GitHubIssues.ParseCreated("https://ghe.corp.com/acme/app/issues/12");
        Assert.Equal(12, created.Number);
        Assert.Contains("/issues/12", created.Url);
    }
}
