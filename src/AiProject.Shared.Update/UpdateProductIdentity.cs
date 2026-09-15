namespace AiProject.Shared.Update;

/// <summary>產品更新通道身份（AD-19／AD-20）。各產品傳入自己的 slug／版號／資產規則。</summary>
public sealed record UpdateProductIdentity(
    string GitHubSlug,
    string CurrentVersion,
    string ExeName,
    string UserAgentProduct,
    string ReleasesUrl,
    string LatestReleaseApiUrl,
    string ReleasesApiUrl,
    string StagingFolderName,
    string DevelopmentMarkerCsproj);
