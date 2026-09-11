using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class IssueMarkdownTests
{
    [Fact]
    public void Normalize_TurnsGithubHtmlImgIntoMarkdown()
    {
        const string body =
            """<img width="3239" height="1549" alt="Image" src="https://github.com/user-attachments/assets/4058bac6-e246-4d0f-bbfc-995382d9ca24" />""";
        var text = IssueMarkdown.NormalizeForDisplay(body);
        Assert.Equal(
            "![Image](https://github.com/user-attachments/assets/4058bac6-e246-4d0f-bbfc-995382d9ca24)",
            text);
    }

    [Fact]
    public void Normalize_UnwrapsAnchorAroundImg()
    {
        const string body =
            """<a href="https://github.com/user-attachments/assets/abc"><img alt="圖" src="https://github.com/user-attachments/assets/abc" /></a>""";
        var text = IssueMarkdown.NormalizeForDisplay(body);
        Assert.Equal("![圖](https://github.com/user-attachments/assets/abc)", text);
        Assert.DoesNotContain("<a", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_DropsJavascriptImg()
    {
        var text = IssueMarkdown.NormalizeForDisplay("""<img src="javascript:alert(1)" alt="x" />""");
        Assert.DoesNotContain("javascript", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("![", text);
    }

    [Fact]
    public void Normalize_KeepsSurroundingMarkdown()
    {
        var text = IssueMarkdown.NormalizeForDisplay(
            "前\n<img src=\"https://example.com/a.png\" alt=\"截圖\" />\n後");
        Assert.Contains("前", text);
        Assert.Contains("![截圖](https://example.com/a.png)", text);
        Assert.Contains("後", text);
    }

    [Fact]
    public void IsSafeHttpUrl_RejectsDataAndRelative()
    {
        Assert.True(IssueMarkdown.IsSafeHttpUrl("https://github.com/a.png"));
        Assert.False(IssueMarkdown.IsSafeHttpUrl("data:image/png;base64,aaa"));
        Assert.False(IssueMarkdown.IsSafeHttpUrl("/relative.png"));
        Assert.False(IssueMarkdown.IsSafeHttpUrl(""));
    }

    [Fact]
    public void FindHttpImageUrls_FromGithubHtmlImg()
    {
        const string body =
            """<img width="3239" height="1549" alt="Image" src="https://github.com/user-attachments/assets/4058bac6-e246-4d0f-bbfc-995382d9ca24" />""";
        var urls = IssueMarkdown.FindHttpImageUrls(body);
        Assert.Equal(
            "https://github.com/user-attachments/assets/4058bac6-e246-4d0f-bbfc-995382d9ca24",
            Assert.Single(urls));
    }

    [Fact]
    public void ApplyImageMap_RewritesUrlToDataUri()
    {
        var md = "![Image](https://github.com/user-attachments/assets/abc)";
        var mapped = IssueMarkdown.ApplyImageMap(
            md,
            new Dictionary<string, string>
            {
                ["https://github.com/user-attachments/assets/abc"] = "data:image/png;base64,aaa",
            });
        Assert.Equal("![Image](data:image/png;base64,aaa)", mapped);
    }

    [Fact]
    public void IsTrustedImageHost_AllowsGithubAttachments()
    {
        Assert.True(IssueMarkdown.IsTrustedImageHost(
            "https://github.com/user-attachments/assets/abc"));
        Assert.True(IssueMarkdown.IsTrustedImageHost(
            "https://private-user-images.githubusercontent.com/a.png"));
        Assert.False(IssueMarkdown.IsTrustedImageHost("https://example.com/a.png"));
        Assert.True(IssueMarkdown.IsTrustedImageHost(
            "https://ghe.corp.com/user-attachments/assets/abc", "ghe.corp.com"));
    }

    [Fact]
    public void TryDataUri_FromPngMagic()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1, 2, 3 };
        Assert.Equal("image/png", IssueImages.GuessMime(png));
        var uri = IssueImages.TryDataUri(png, "text/html");
        Assert.StartsWith("data:image/png;base64,", uri);
        Assert.Null(IssueImages.TryDataUri([0x3C, 0x68, 0x74, 0x6D, 0x6C], "text/html"));
    }
}
