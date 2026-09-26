namespace AiProject.Console.Core;

public static class AppInfo
{
    public const string Version = "0.6.18";
    public const string Product = "AI_Project 控制台";
    public const string GitHubOwner = "sjvann";
    public const string GitHubRepo = "AI_Project_Console";
    public const string SettingsDirName = "AI_Project_Console";
    public const string ManifestName = "ai-project.json";
    public const string RuntimeDirName = ".ai_project";
    public const string ExeName = "AI_Project_Console.exe";
    public const string UnixExeName = "AI_Project_Console";
    public const string MacAppBundleName = "AI_Project_Console.app";
    public const string InstalledMarkerName = "console-installed.marker";
    public const string AppUserModelId = "sjvann.AIProjectConsole";

    public static string WindowTitle(string? workspaceName = null) =>
        string.IsNullOrWhiteSpace(workspaceName)
            ? Product
            : workspaceName.Trim() + " · " + Product;

    public static string GitHubSlug => GitHubOwner + "/" + GitHubRepo;
    public static string ReleasesUrl => $"https://github.com/{GitHubSlug}/releases";
    public static string LatestReleaseApiUrl => $"https://api.github.com/repos/{GitHubSlug}/releases/latest";
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{GitHubSlug}/releases?per_page=30";

    public static AiProject.Shared.Update.UpdateProductIdentity UpdateIdentity { get; } = new(
        GitHubSlug,
        Version,
        ExeName,
        SettingsDirName,
        ReleasesUrl,
        LatestReleaseApiUrl,
        ReleasesApiUrl,
        "AI_Project_Console-update",
        "AiProject.Console.App.csproj");
}
