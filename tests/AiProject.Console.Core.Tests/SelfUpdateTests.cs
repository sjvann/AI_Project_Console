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
    public void ParseLatest_EmptyAssets_OpensReleasesForInstalled()
    {
        const string json = """
            {
              "tag_name": "v0.6.10",
              "name": "v0.6.10",
              "html_url": "https://github.com/sjvann/AI_Project_Console/releases/tag/v0.6.10",
              "assets": []
            }
            """;
        var update = SelfUpdate.ParseLatest(json, "0.6.9", "win-x64");
        Assert.NotNull(update);
        Assert.Null(update!.SetupAsset);
        Assert.Null(update.ZipAsset);
        Assert.Equal(UpdateApplyMode.OpenReleases, SelfUpdate.ResolveApplyMode(update, InstallKind.Installed));
        var hint = SelfUpdate.CannotApplyHint(update, InstallKind.Installed);
        Assert.Contains("沒有適用於 " + SelfUpdate.RuntimeId() + " 的安裝檔", hint);
        Assert.Contains("-setup.exe", hint);
    }

    [Fact]
    public void DetectInstallKind_RecognizesDevBinFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-upd-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, "bin", "Release", "net10.0", "win-x64");
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
    public void BuildInstallerRestartScript_WaitsThenRelaunches()
    {
        var setup = Path.Combine(Path.GetTempPath(), "AI_Project_Console-0.4.0-win-x64-setup.exe");
        var dir = Path.Combine(Path.GetTempPath(), "Programs", "AI_Project_Console");
        var exe = Path.Combine(dir, "AI_Project_Console.exe");
        var script = SelfUpdate.BuildInstallerRestartScript(setup, dir, 4242, exe);
        Assert.Contains("Get-Process -Id 4242", script, StringComparison.Ordinal);
        Assert.Contains("-Wait", script, StringComparison.Ordinal);
        Assert.Contains("/SILENT", script, StringComparison.Ordinal);
        Assert.Contains("/DIR=", script, StringComparison.Ordinal);
        var visible = SelfUpdate.BuildInstallerRestartScript(setup, dir, 4242, exe, silent: false);
        Assert.DoesNotContain("/SILENT", visible, StringComparison.Ordinal);
        var silentArgs = SelfUpdate.BuildInstallerArguments(dir, silent: true, pinDirectory: true);
        Assert.Contains("/SILENT", silentArgs, StringComparison.Ordinal);
        Assert.Contains("/DIR=", silentArgs, StringComparison.Ordinal);
        var visibleArgs = SelfUpdate.BuildInstallerArguments(dir, silent: false, pinDirectory: false);
        Assert.Equal("/NORESTART", visibleArgs);
        Assert.Contains("Start-Process -FilePath '" + Path.GetFullPath(exe) + "'", script, StringComparison.Ordinal);
        Assert.Contains("Get-Process -Name 'AI_Project_Console'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseLatest_ThrowsOnGithubApiMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SelfUpdate.ParseLatest("""{"message":"API rate limit exceeded"}""", "0.3.2"));
        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseLatest_SkipsPrereleaseUnlessRequested()
    {
        const string json = """
            {
              "tag_name": "v0.5.0-rc.1",
              "name": "0.5.0 RC1",
              "html_url": "https://github.com/sjvann/AI_Project_Console/releases/tag/v0.5.0-rc.1",
              "prerelease": true,
              "assets": []
            }
            """;
        Assert.Null(SelfUpdate.ParseLatest(json, "0.4.0", "win-x64"));
        var update = SelfUpdate.ParseLatest(json, "0.4.0", "win-x64", includePrerelease: true);
        Assert.NotNull(update);
        Assert.Equal("v0.5.0-rc.1", update!.Tag);
        Assert.True(update.Prerelease);
    }

    [Fact]
    public void ParseLatest_PicksNewestIncludingRcFromList()
    {
        const string json = """
            [
              {
                "tag_name": "v0.4.1",
                "name": "0.4.1",
                "html_url": "https://example.com/v0.4.1",
                "prerelease": false,
                "assets": []
              },
              {
                "tag_name": "v0.5.0-rc.1",
                "name": "0.5.0 RC1",
                "html_url": "https://example.com/v0.5.0-rc.1",
                "prerelease": true,
                "assets": []
              },
              {
                "tag_name": "v0.3.9",
                "name": "draft",
                "html_url": "https://example.com/draft",
                "draft": true,
                "prerelease": false,
                "assets": []
              }
            ]
            """;
        var stable = SelfUpdate.ParseLatest(json, "0.4.0", "win-x64");
        Assert.NotNull(stable);
        Assert.Equal("v0.4.1", stable!.Tag);
        Assert.False(stable.Prerelease);

        var withRc = SelfUpdate.ParseLatest(json, "0.4.0", "win-x64", includePrerelease: true);
        Assert.NotNull(withRc);
        Assert.Equal("v0.5.0-rc.1", withRc!.Tag);
        Assert.True(withRc.Prerelease);
    }

    [Fact]
    public void ParseLatest_IgnoresDraftEvenWhenIncludingRc()
    {
        const string json = """
            [
              {
                "tag_name": "v9.0.0",
                "name": "draft",
                "html_url": "https://example.com/draft",
                "draft": true,
                "prerelease": false,
                "assets": []
              }
            ]
            """;
        Assert.Null(SelfUpdate.ParseLatest(json, "0.4.0", "win-x64", includePrerelease: true));
    }

    [Fact]
    public void ResolveLocalFileMode_UsesExtension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-upd-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var setup = Path.Combine(dir, "AI_Project_Console-0.5.0-win-x64-setup.exe");
        var zip = Path.Combine(dir, "AI_Project_Console-0.5.0-win-x64.zip");
        File.WriteAllText(setup, "");
        File.WriteAllText(zip, "");
        try
        {
            Assert.Equal(UpdateApplyMode.Installer, SelfUpdate.ResolveLocalFileMode(setup, InstallKind.Installed));
            Assert.Equal(UpdateApplyMode.PortableZip, SelfUpdate.ResolveLocalFileMode(zip, InstallKind.Portable));
            Assert.Equal(UpdateApplyMode.Installer, SelfUpdate.ResolveLocalFileMode(setup, InstallKind.Development));
            Assert.Equal(UpdateApplyMode.None, SelfUpdate.ResolveLocalFileMode(Path.Combine(dir, "missing.exe"), InstallKind.Installed));
            var hint = SelfUpdate.CannotApplyLocalFileHint(InstallKind.Development, setup);
            Assert.Contains("開發目錄", hint);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StageUpdateFile_CopiesAndUnblocks()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "AI_Project_Console-0.6.10-win-x64-setup.exe");
        File.WriteAllText(src, "setup");
        try
        {
            var staged = SelfUpdate.StageUpdateFile(src);
            Assert.True(File.Exists(staged));
            Assert.NotEqual(src, staged);
            Assert.Equal("setup", File.ReadAllText(staged));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            TryDelete(Path.Combine(Path.GetTempPath(), "AI_Project_Console-update", Path.GetFileName(src)));
        }
    }

    [Fact]
    public void StageUpdateFile_SkipsCopyWhenAlreadyInStagingFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AI_Project_Console-update");
        Directory.CreateDirectory(dir);
        var src = Path.Combine(dir, "AI_Project_Console-stage-self-" + Guid.NewGuid().ToString("N")[..8] + "-setup.exe");
        File.WriteAllText(src, "setup");
        try
        {
            var staged = SelfUpdate.StageUpdateFile(src);
            Assert.Equal(Path.GetFullPath(src), staged);
            Assert.Equal("setup", File.ReadAllText(staged));
        }
        finally
        {
            TryDelete(src);
        }
    }

    [Fact]
    public void StageUpdateFile_UsesUniqueNameWhenDestLocked()
    {
        var staging = Path.Combine(Path.GetTempPath(), "AI_Project_Console-update");
        Directory.CreateDirectory(staging);
        var name = "AI_Project_Console-lock-" + Guid.NewGuid().ToString("N")[..8] + "-setup.exe";
        var dest = Path.Combine(staging, name);
        var srcDir = Path.Combine(Path.GetTempPath(), "ai-console-stage-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(srcDir);
        var src = Path.Combine(srcDir, name);
        File.WriteAllText(src, "new");
        File.WriteAllText(dest, "old");
        string? staged = null;
        try
        {
            using (new FileStream(dest, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                staged = SelfUpdate.StageUpdateFile(src);
                Assert.True(File.Exists(staged));
                Assert.NotEqual(Path.GetFullPath(dest), staged);
                Assert.Equal("new", File.ReadAllText(staged));
            }
        }
        finally
        {
            Directory.Delete(srcDir, recursive: true);
            TryDelete(dest);
            if (staged is not null)
                TryDelete(staged);
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // ignore leftover temp files
        }
    }
}
