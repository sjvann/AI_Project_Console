using AiProject.Console.Core;

namespace AiProject.Console.Core.Tests;

public class AppIdentityTests
{
    [Fact]
    public void WindowTitle_OmitsVersion()
    {
        Assert.Equal(AppInfo.Product, AppInfo.WindowTitle());
        Assert.Equal(AppInfo.Product, AppInfo.WindowTitle(""));
        Assert.Equal("CareMatrix · " + AppInfo.Product, AppInfo.WindowTitle("CareMatrix"));
        Assert.DoesNotContain(AppInfo.Version, AppInfo.WindowTitle());
        Assert.DoesNotContain(AppInfo.Version, AppInfo.WindowTitle("專案"));
        Assert.DoesNotContain(" v", AppInfo.WindowTitle("專案"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerDisplayNames_OmitVersion()
    {
        var iss = File.ReadAllText(Path.Combine(FindRepoRoot(), "installer", "windows", "setup.iss"));
        Assert.Contains("#define MyAppName \"" + AppInfo.Product + "\"", iss);
        Assert.Contains("UninstallDisplayName={#MyAppName}", iss);
        Assert.Contains("AppVerName={#MyAppName}", iss);
        Assert.Contains("VersionInfoProductName={#MyAppName}", iss);
        Assert.DoesNotContain("AppVerName={#MyAppName} {#MyAppVersion}", iss);
        Assert.DoesNotContain("AppName={#MyAppName} {#MyAppVersion}", iss);
        Assert.Contains("AppUserModelID: \"{#MyAppUserModelId}\"", iss);
        Assert.Contains("{userprograms}\\{#MyAppName} v*.lnk", iss);
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "AiProject.Console.slnx")))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent))
                break;
            dir = parent;
        }
        throw new DirectoryNotFoundException("找不到含 AiProject.Console.slnx 的倉根目錄");
    }
}
