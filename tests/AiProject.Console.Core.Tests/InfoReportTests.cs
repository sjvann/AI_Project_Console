using AiProject.Console.Core;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class InfoReportTests
{
    [Fact]
    public void ReleaseItem_HidesDuplicateTitleAndFormatsTime()
    {
        var same = new ReleaseItem("v0.4.4", "v0.4.4", true, false, false, "2026-08-26T08:48:00Z");
        Assert.Equal("", same.Title);
        Assert.Contains("Latest", same.Line());
        Assert.DoesNotContain("v0.4.4 v0.4.4", same.Line());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", same.PublishedText);

        var named = new ReleaseItem("v0.4.4", "控制台", false, true, true, "");
        Assert.Equal("控制台", named.Title);
        Assert.Contains("控制台", named.Line());
        Assert.Contains("draft", named.Line());
        Assert.Contains("pre", named.Line());
    }

    [Fact]
    public void ReleaseListView_CopyText_OmitsEmptyTitleAndKeepsUrl()
    {
        var list = new ReleaseListView(
            "sjvann/AI_Project_Console",
            "https://github.com/sjvann/AI_Project_Console/releases",
            [new ReleaseItem("v0.4.4", "v0.4.4", true, false, false, "2026-08-26T08:48:00Z")]);
        var text = list.ToText();
        Assert.Contains("GitHub Releases · sjvann/AI_Project_Console", text);
        Assert.Contains("v0.4.4", text);
        Assert.DoesNotContain("v0.4.4  v0.4.4", text);
        Assert.Contains("https://github.com/sjvann/AI_Project_Console/releases", text);
    }

    [Fact]
    public void JobResultView_IsDense_UsesNewlineAndLength()
    {
        Assert.False(JobResultView.IsDense(null));
        Assert.False(JobResultView.IsDense("已接受 #12"));
        Assert.True(JobResultView.IsDense("fetch OK\npull --rebase OK"));
        Assert.True(JobResultView.IsDense(new string('x', 161)));
        Assert.Equal("Release 列表", JobResultView.CleanTitle("Release 列表…"));
        Assert.False(new JobResultView("同步", "ok", "fetch OK", "fetch OK").HasDetail);
        Assert.True(new JobResultView("同步", "ok", "fetch OK", "fetch OK\npull OK").HasDetail);
    }

    [Fact]
    public void DeployStatusView_NoProject_AndLocalOnly()
    {
        var empty = DeployConfigResolver.StatusView(null);
        Assert.Equal("部署狀態", empty.Title);
        Assert.Equal("尚未選擇專案", empty.Headline);
        Assert.Contains("尚未選擇專案", empty.Text);
        Assert.Empty(empty.Sections);

        var root = Path.Combine(Path.GetTempPath(), "ai-console-deploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "demo",
                Services = [],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = new System.Text.Json.Nodes.JsonObject(),
                Scan = new ScanResult(root, []),
            };
            var report = DeployConfigResolver.StatusView(catalog);
            Assert.Equal("info", report.Tone);
            Assert.Equal("edit-deploy", report.PrimaryAction);
            Assert.Contains(report.Sections, s => s.Id == "target");
            Assert.Contains("本機執行", report.HeadlineDetail);
            Assert.Equal(report.Text, DeployConfigResolver.StatusReport(catalog));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task GithubStatusView_NoProject()
    {
        var report = await GitHubService.StatusViewAsync(null);
        Assert.Equal("GitHub 狀態", report.Title);
        Assert.Equal("尚未選擇專案", report.Headline);
        Assert.Equal("info", report.Tone);
        Assert.Contains("尚未選擇專案", report.Text);
        Assert.Equal(report.Text, await GitHubService.StatusReportAsync(null));
    }
}
