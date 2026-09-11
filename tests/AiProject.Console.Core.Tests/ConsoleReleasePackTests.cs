using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class ConsoleReleasePackTests
{
    [Theory]
    [InlineData("v0.6.10", "0.6.10")]
    [InlineData("0.6.10", "0.6.10")]
    [InlineData("v1.2.3-rc.1", "1.2.3-rc.1")]
    public void BareVersion_StripsPrefix(string tag, string expected)
    {
        Assert.Equal(expected, ConsoleReleasePack.BareVersion(tag));
    }

    [Fact]
    public void LooksPackable_NeedsScriptAndIss()
    {
        var root = NewTemp();
        try
        {
            Assert.False(ConsoleReleasePack.LooksPackable(root));
            Directory.CreateDirectory(Path.Combine(root, "scripts"));
            Directory.CreateDirectory(Path.Combine(root, "installer", "windows"));
            File.WriteAllText(Path.Combine(root, "scripts", "pack-win.ps1"), "");
            Assert.False(ConsoleReleasePack.LooksPackable(root));
            File.WriteAllText(Path.Combine(root, "installer", "windows", "setup.iss"), "");
            Assert.True(ConsoleReleasePack.LooksPackable(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindExisting_PicksSetupAndZip()
    {
        var root = NewTemp();
        try
        {
            var dist = Path.Combine(root, "dist");
            Directory.CreateDirectory(dist);
            var setup = Path.Combine(dist, "AI_Project_Console-0.6.10-win-x64-setup.exe");
            var zip = Path.Combine(dist, "AI_Project_Console-0.6.10-win-x64.zip");
            File.WriteAllText(setup, "setup");
            File.WriteAllText(zip, "zip");
            var found = ConsoleReleasePack.FindExisting(root, "v0.6.10");
            Assert.True(found.HasSetup);
            Assert.True(found.HasZip);
            Assert.Equal(setup, found.SetupPath);
            Assert.Equal(zip, found.ZipPath);
            Assert.Equal(2, found.ExistingPaths().Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void HasSetupAsset_MatchesSelfUpdateRule()
    {
        Assert.True(ConsoleReleasePack.HasSetupAsset(
            ["C:\\dist\\AI_Project_Console-0.6.10-win-x64-setup.exe"]));
        Assert.False(ConsoleReleasePack.HasSetupAsset(
            ["C:\\dist\\AI_Project_Console-0.6.10-win-x64.zip"]));
        Assert.True(ConsoleReleasePack.RequiresInstaller(packable: true, draft: false));
        Assert.False(ConsoleReleasePack.RequiresInstaller(packable: true, draft: true));
        Assert.False(ConsoleReleasePack.RequiresInstaller(packable: false, draft: false));
    }

    [Fact]
    public void StampVersion_UpdatesKnownFiles()
    {
        Assert.Contains("Version = \"0.7.0\"", ConsoleReleasePack.StampVersion(
            "public const string Version = \"0.6.9\";", "appinfo", "0.7.0"));
        var csproj = ConsoleReleasePack.StampVersion(
            "<Version>0.6.9</Version>\n<InformationalVersion>0.6.9</InformationalVersion>",
            "csproj",
            "0.7.0");
        Assert.Contains("<Version>0.7.0</Version>", csproj);
        Assert.Contains("<InformationalVersion>0.7.0</InformationalVersion>", csproj);
        Assert.Contains("MyAppVersion \"0.7.0\"", ConsoleReleasePack.StampVersion(
            "#define MyAppVersion \"0.6.9\"", "iss", "0.7.0"));
        Assert.Contains("$Version = \"0.7.0\"", ConsoleReleasePack.StampVersion(
            "[string]$Version = \"0.6.9\",", "ps1", "0.7.0"));
    }

    [Fact]
    public void MergeNotes_PrependsDownloadWhenMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-notes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var setup = Path.Combine(dir, "AI_Project_Console-0.6.10-win-x64-setup.exe");
            var zip = Path.Combine(dir, "AI_Project_Console-0.6.10-win-x64.zip");
            File.WriteAllText(setup, "x");
            File.WriteAllText(zip, "x");
            var packed = new PackedReleaseAssets("0.6.10", "win-x64", setup, zip);
            var notes = ConsoleReleasePack.MergeNotes("修正自動更新。", packed);
            Assert.StartsWith("修正自動更新。", notes);
            Assert.Contains("-setup.exe", notes);
            Assert.Contains(".zip", notes);

            var already = ConsoleReleasePack.MergeNotes("已含 AI_Project_Console-0.6.10-win-x64-setup.exe", packed);
            Assert.Equal("已含 AI_Project_Console-0.6.10-win-x64-setup.exe", already);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParseReleaseAssetNames_ReadsJson()
    {
        var names = GitHubService.ParseReleaseAssetNames("""
            {"assets":[{"name":"AI_Project_Console-0.6.9-win-x64-setup.exe"},{"name":"AI_Project_Console-0.6.9-win-x64.zip"}]}
            """);
        Assert.Equal(2, names.Count);
        Assert.True(ConsoleReleasePack.HasSetupAsset(names));
        Assert.Empty(GitHubService.ParseReleaseAssetNames("""{"assets":[]}"""));
    }

    [Fact]
    public void InterpretPackLine_MapsStagesAndSkipsNoise()
    {
        var (stage, user) = ConsoleReleasePack.InterpretPackLine("PACK:publish");
        Assert.Equal("publish", stage);
        Assert.Contains("編譯", user);

        var noise = ConsoleReleasePack.InterpretPackLine("Workload updates are available. For more information, run:");
        Assert.Null(noise.UserLine);

        var restore = ConsoleReleasePack.InterpretPackLine("  Determining projects to restore...");
        Assert.Equal("publish", restore.StageId);
        Assert.Contains("還原", restore.UserLine);

        var error = ConsoleReleasePack.InterpretPackLine("error CS0001: bad");
        Assert.Contains("error CS0001", error.UserLine);
    }

    [Fact]
    public void FormatPackError_KeepsFullLogAndMapsInno()
    {
        var full = ConsoleReleasePack.FormatPackError("line1\nline2\nline3", "");
        Assert.Contains("line3", full);
        Assert.DoesNotContain("請先填寫有效版號", full);

        var inno = ConsoleReleasePack.FormatPackError("", "找不到 Inno Setup 6（ISCC.exe）。請先安裝 https://jrsoftware.org/isinfo.php");
        Assert.Contains("jrsoftware.org", inno);
        Assert.Contains('\n', inno);
    }

    [Fact]
    public void ReleaseRunState_TracksStepsAndLog()
    {
        var run = ReleaseRunState.PackAndPublish();
        run.Begin();
        Assert.Equal(ReleaseStepStatus.Active, run.Steps[0].Status);
        run.Apply(ReleaseRunState.StageLine("publish", "正在編譯 Windows 執行檔（可能要 1–3 分鐘）…"));
        Assert.Equal(ReleaseStepStatus.Done, run.Steps.First(s => s.Id == "version").Status);
        Assert.Equal(ReleaseStepStatus.Active, run.Steps.First(s => s.Id == "publish").Status);
        Assert.Contains("編譯", run.Log);
        run.Apply("正在編譯 Windows 執行檔（可能要 1–3 分鐘）…");
        Assert.Single(run.Log.Split('\n'));
        run.Fail("找不到 Inno Setup（ISCC.exe）。\n請安裝。");
        Assert.False(run.Busy);
        Assert.Equal("error", run.Tone);
        Assert.Equal(ReleaseStepStatus.Error, run.Steps.First(s => s.Id == "publish").Status);
        Assert.Equal(ReleaseStepStatus.Skipped, run.Steps.First(s => s.Id == "upload").Status);
        Assert.Equal("找不到 Inno Setup（ISCC.exe）。", run.Headline);
        Assert.Contains("請安裝。", run.CopyText);
    }

    [Fact]
    public void ReleaseRunState_PublishOnlySkipsPack()
    {
        var run = ReleaseRunState.PublishOnly();
        run.Begin();
        Assert.Equal(ReleaseStepStatus.Skipped, run.Steps.First(s => s.Id == "publish").Status);
        Assert.Equal(ReleaseStepStatus.Active, run.Steps.First(s => s.Id == "upload").Status);
        run.Succeed("https://github.com/sjvann/AI_Project_Console/releases/tag/v0.6.10");
        Assert.Equal(ReleaseStepStatus.Done, run.Steps.First(s => s.Id == "upload").Status);
        Assert.False(run.Busy);
    }

    [Fact]
    public void InterpretGhLine_MapsUpload()
    {
        var line = ConsoleReleasePack.InterpretGhLine("Uploading AI_Project_Console-0.6.10-win-x64-setup.exe");
        Assert.Contains("PACKSTAGE:upload", line);
        var url = ConsoleReleasePack.InterpretGhLine("https://github.com/sjvann/AI_Project_Console/releases/tag/v0.6.10");
        Assert.Contains("Release 已建立", url);
    }

    static string NewTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
