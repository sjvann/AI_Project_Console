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
    public void LooksLikeReleaseExists_FromGhError()
    {
        Assert.True(GitHubService.LooksLikeReleaseExists("Release.tag_name already exists"));
        Assert.True(GitHubService.LooksLikeReleaseExists("{\"code\":\"already_exists\"}"));
        Assert.True(GitHubService.LooksLikeReleaseExists("HTTP 422: Validation Failed"));
        Assert.False(GitHubService.LooksLikeReleaseExists("not found"));
    }

    static string NewTemp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
