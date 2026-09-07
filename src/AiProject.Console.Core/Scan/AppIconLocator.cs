using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Scan;

/// <summary>
/// 從本機專案目錄找出 APP 圖示（csproj ApplicationIcon、wwwroot favicon、Assets/app.ico）。
/// </summary>
public static class AppIconLocator
{
    public const int MaxBytes = 512 * 1024;

    private static readonly string[] Candidates =
    [
        Path.Combine("wwwroot", "favicon.ico"),
        Path.Combine("wwwroot", "favicon.png"),
        Path.Combine("wwwroot", "favicon.svg"),
        Path.Combine("Assets", "app.ico"),
        "app.ico",
    ];

    public static string? FindAbsolute(string projectDir, string? applicationIcon = null)
    {
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            return null;

        if (!string.IsNullOrWhiteSpace(applicationIcon))
        {
            var declared = Path.GetFullPath(Path.Combine(projectDir, applicationIcon.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(declared))
                return declared;
        }

        foreach (var rel in Candidates)
        {
            var full = Path.GetFullPath(Path.Combine(projectDir, rel));
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    public static string RelPath(string workspaceRoot, string absolutePath)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(absolutePath))
                .Replace('\\', '/');
        }
        catch (Exception)
        {
            return absolutePath.Replace('\\', '/');
        }
    }

    public static string? FindRel(string workspaceRoot, string projectDir, string? applicationIcon = null)
    {
        var abs = FindAbsolute(projectDir, applicationIcon);
        return abs is null ? null : RelPath(workspaceRoot, abs);
    }

    /// <summary>
    /// 工作區標題用的圖示：清單 <c>icon</c>、預設前端、Web UI APP，再退回其他有圖示的服務。
    /// </summary>
    public static string? WorkspaceIcon(ProjectCatalog catalog)
    {
        var declared = JsonUtil.Pick(JsonUtil.Str(catalog.Manifest["icon"]));
        if (!string.IsNullOrEmpty(declared))
        {
            var full = Path.IsPathRooted(declared)
                ? Path.GetFullPath(declared)
                : Path.GetFullPath(Path.Combine(catalog.Root, declared.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(full))
                return RelPath(catalog.Root, full);
        }

        if (!string.IsNullOrEmpty(catalog.Frontend))
        {
            var front = catalog.Services.FirstOrDefault(s =>
                string.Equals(s.Id, catalog.Frontend, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(front?.IconPath))
                return front.IconPath;
            if (front is not null && !string.IsNullOrEmpty(front.HostedBy))
            {
                var host = catalog.Services.FirstOrDefault(s =>
                    string.Equals(s.Id, front.HostedBy, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(host?.IconPath))
                    return host.IconPath;
            }
        }

        var webUi = catalog.Projects.FirstOrDefault(p => p.IsUi && p.IsWeb && !string.IsNullOrEmpty(p.IconPath));
        if (webUi is not null)
            return webUi.IconPath;
        var ui = catalog.Projects.FirstOrDefault(p => p.IsUi && !string.IsNullOrEmpty(p.IconPath));
        if (ui is not null)
            return ui.IconPath;
        return catalog.Services.FirstOrDefault(s => !string.IsNullOrEmpty(s.IconPath))?.IconPath;
    }

    public static string? ResolveAbsolute(string workspaceRoot, string? relOrAbs)
    {
        if (string.IsNullOrWhiteSpace(relOrAbs))
            return null;
        var full = Path.IsPathRooted(relOrAbs)
            ? Path.GetFullPath(relOrAbs)
            : Path.GetFullPath(Path.Combine(workspaceRoot, relOrAbs.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(full) ? full : null;
    }

    public static string? TryDataUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaxBytes)
                return null;
            var bytes = File.ReadAllBytes(path);
            return $"data:{Mime(path)};base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static string Mime(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream",
        };
}
