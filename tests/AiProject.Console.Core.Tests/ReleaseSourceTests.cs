using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tests;

public class ReleaseSourceTests
{
    [Theory]
    [InlineData("AI_Project_Console-0.6.10-src.zip", true)]
    [InlineData("app-1.0.0-src.tar.gz", true)]
    [InlineData("source.zip", true)]
    [InlineData("Source code.zip", true)]
    [InlineData("AI_Project_Console-0.6.10-win-x64.zip", false)]
    [InlineData("AI_Project_Console-0.6.10-win-x64-setup.exe", false)]
    [InlineData(@"C:\dist\AI_Project_Console-0.6.10-win-x64.zip", false)]
    [InlineData(@"D:\out\repo-1.2.3-src.zip", true)]
    public void IsSourceAsset_MatchesSrcArchivesOnly(string path, bool expected)
    {
        Assert.Equal(expected, ReleaseSource.IsSourceAsset(path));
    }

    [Fact]
    public void SelectUploadAssets_OmitsSourceUnlessRequested()
    {
        string[] paths =
        [
            @"C:\dist\AI_Project_Console-0.6.10-win-x64-setup.exe",
            @"C:\dist\AI_Project_Console-0.6.10-win-x64.zip",
            @"C:\dist\AI_Project_Console-0.6.10-src.zip",
        ];
        var omitted = ReleaseSource.SelectUploadAssets(paths, includeSource: false);
        Assert.Equal(2, omitted.Count);
        Assert.DoesNotContain(omitted, ReleaseSource.IsSourceAsset);

        var kept = ReleaseSource.SelectUploadAssets(paths, includeSource: true);
        Assert.Equal(3, kept.Count);
    }

    [Fact]
    public void FileName_UsesRepoAndBareVersion()
    {
        Assert.Equal("AI_Project_Console-0.6.10-src.zip", ReleaseSource.FileName("AI_Project_Console", "v0.6.10"));
        Assert.Equal("app-1.2.3-src.zip", ReleaseSource.FileName("app", "1.2.3"));
    }

    [Fact]
    public void MergeNotes_WarnsWhenSourceOmitted()
    {
        var notes = ReleaseSource.MergeNotes("修正自動更新。", includeSource: false);
        Assert.StartsWith("修正自動更新。", notes);
        Assert.Contains("Source code", notes);
        Assert.DoesNotContain("-src.zip", notes);

        var already = ReleaseSource.MergeNotes("請勿下載 Source code。", includeSource: false);
        Assert.Equal("請勿下載 Source code。", already);
    }

    [Fact]
    public void MergeNotes_MentionsArchiveWhenIncluded()
    {
        var notes = ReleaseSource.MergeNotes("", includeSource: true, sourceZipName: "app-1.0.0-src.zip");
        Assert.Contains("app-1.0.0-src.zip", notes);
        Assert.Contains("git archive", notes);
    }

    [Fact]
    public async Task ArchiveAsync_WritesZipFromHead()
    {
        if (!CliUtil.CommandExists("git"))
            return;

        var root = Path.Combine(Path.GetTempPath(), "ai-release-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Git(root, "init");
            await Git(root, "config", "user.email", "test@example.com");
            await Git(root, "config", "user.name", "test");
            await Git(root, "config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(root, "README.md"), "hello");
            await Git(root, "add", "README.md");
            await Git(root, "commit", "-m", "init");
            var dest = Path.Combine(root, "out-1.0.0-src.zip");
            var created = await ReleaseSource.ArchiveAsync(root, "v1.0.0", dest);
            Assert.Equal(dest, created);
            Assert.True(File.Exists(dest));
            Assert.True(new FileInfo(dest).Length > 0);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    static async Task Git(string root, params string[] args)
    {
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync("git", args, cwd: root, timeoutMs: 30_000);
        if (code != 0)
            throw new InvalidOperationException(string.Join('\n', new[] { stdout, stderr }.Where(s => s.Length > 0)));
    }
}
