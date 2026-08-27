namespace AiProject.Console.Core;

public static class AppInfo
{
    public const string Version = "0.6.3";
    public const string Product = "AI_Project 控制台";
    public const string GitHubOwner = "sjvann";
    public const string GitHubRepo = "AI_Project_Console";
    public const string SettingsDirName = "AI_Project_Console";
    public const string ManifestName = "ai-project.json";
    public const string RuntimeDirName = ".ai_project";
    public const string ExeName = "AI_Project_Console.exe";
    public const string AppUserModelId = "sjvann.AIProjectConsole";

    public static string GitHubSlug => GitHubOwner + "/" + GitHubRepo;
    public static string ReleasesUrl => $"https://github.com/{GitHubSlug}/releases";
    public static string LatestReleaseApiUrl => $"https://api.github.com/repos/{GitHubSlug}/releases/latest";
}
