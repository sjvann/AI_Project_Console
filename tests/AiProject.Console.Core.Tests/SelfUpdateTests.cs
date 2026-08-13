using AiProject.Console.Core.Update;

namespace AiProject.Console.Core.Tests;

public class SelfUpdateTests
{
    private const string SampleJson = """
        {
          "tag_name": "v0.4.0",
          "name": "0.4.0",
          "html_url": "https://github.com/sjvann/AI_Project_Console/releases/tag/v0.4.0",
          "assets": [
            {
              "name": "AI_Project_Console-0.4.0-win-x64-setup.exe",
              "browser_download_url": "https://example.com/setup.exe",
              "size": 10
            },
            {
              "name": "AI_Project_Console-0.4.0-win-x64.zip",
              "browser_download_url": "https://example.com/app.zip",
              "size": 20
            },
            {
              "name": "AI_Project_Console-0.4.0-osx-arm64.zip",
              "browser_download_url": "https://example.com/osx.zip",
              "size": 30
            }
          ]
        }
        """;

    [Fact]
    public void ParseLatest_PicksWindowsAssetsAndIgnoresCurrent()
    {
        var update = SelfUpdate.ParseLatest(SampleJson, "0.3.2", "win-x64");
        Assert.NotNull(update);
        Assert.Equal("v0.4.0", update!.Tag);
        Assert.Equal("AI_Project_Console-0.4.0-win-x64-setup.exe", update.SetupAsset?.Name);
        Assert.Equal("AI_Project_Console-0.4.0-win-x64.zip", update.ZipAsset?.Name);
    }

    [Fact]
    public void ParseLatest_ReturnsNullWhenNotNewer()
    {
        Assert.Null(SelfUpdate.ParseLatest(SampleJson, "0.4.0", "win-x64"));
        Assert.Null(SelfUpdate.ParseLatest(SampleJson, "0.5.0", "win-x64"));
    }

    [Fact]
    public void ResolveApplyMode_UsesInstallerWhenInstalled()
    {
        var update = SelfUpdate.ParseLatest(SampleJson, "0.3.2", "win-x64")!;
        Assert.Equal(UpdateApplyMode.Installer, SelfUpdate.ResolveApplyMode(update, InstallKind.Installed));
        Assert.Equal(UpdateApplyMode.PortableZip, SelfUpdate.ResolveApplyMode(update, InstallKind.Portable));
        Assert.Equal(UpdateApplyMode.OpenReleases, SelfUpdate.ResolveApplyMode(update, InstallKind.Development));
    }

    [Fact]
    public void DetectInstallKind_RecognizesDevBinFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-upd-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "bin", "Release", "net8.0", "win-x64");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(InstallKind.Development, SelfUpdate.DetectInstallKind(dir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DetectInstallKind_RecognizesInnoUninstaller()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-upd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "");
            Assert.Equal(InstallKind.Installed, SelfUpdate.DetectInstallKind(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParseLatest_ThrowsOnGithubApiMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SelfUpdate.ParseLatest("""{"message":"API rate limit exceeded"}""", "0.3.2"));
        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
