using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Scan;

/// <summary>
/// 從本機專案目錄找出 APP 圖示（csproj ApplicationIcon、版面宣告的 icon、品牌圖、wwwroot favicon、Assets/app.ico）。
/// </summary>
public static class AppIconLocator
{
    public const int MaxBytes = 512 * 1024;
    private const int MaxMarkupBytes = 256 * 1024;

    private static readonly string[] LayoutFiles =
    [
        Path.Combine("Pages", "Shared", "_Layout.cshtml"),
        Path.Combine("Pages", "_Layout.cshtml"),
        Path.Combine("Views", "Shared", "_Layout.cshtml"),
        Path.Combine("Shared", "_Layout.cshtml"),
        Path.Combine("Components", "App.razor"),
        "App.razor",
        Path.Combine("Pages", "_Host.cshtml"),
        Path.Combine("wwwroot", "index.html"),
        "index.html",
    ];

    private static readonly string[] Candidates =
    [
        Path.Combine("wwwroot", "brand", "app-icon.svg"),
        Path.Combine("wwwroot", "brand", "app-icon.png"),
        Path.Combine("wwwroot", "favicon.svg"),
        Path.Combine("wwwroot", "favicon.png"),
        Path.Combine("wwwroot", "favicon.ico"),
        Path.Combine("Assets", "app.ico"),
        "app.ico",
    ];

    private static readonly Regex LinkTag = new(
        @"<link\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IconRel = new(
        @"\brel\s*=\s*[""'](?:shortcut icon|icon)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HrefAttr = new(
        @"\bhref\s*=\s*[""']([^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var fromMarkup = FindDeclaredIcon(projectDir);
        if (fromMarkup is not null)
            return fromMarkup;

        foreach (var rel in Candidates)
        {
            var full = Path.GetFullPath(Path.Combine(projectDir, rel));
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    private static string? FindDeclaredIcon(string projectDir)
    {
        foreach (var rel in LayoutFiles)
        {
            var path = Path.Combine(projectDir, rel);
            if (!File.Exists(path))
                continue;
            string text;
            try
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > MaxMarkupBytes)
                    continue;
                text = File.ReadAllText(path);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (Match tag in LinkTag.Matches(text))
            {
                if (!IconRel.IsMatch(tag.Value))
                    continue;
                var href = HrefAttr.Match(tag.Value);
                if (!href.Success)
                    continue;
                var resolved = ResolveWebHref(projectDir, href.Groups[1].Value);
                if (resolved is not null)
                    return resolved;
            }
        }
        return null;
    }

    private static string? ResolveWebHref(string projectDir, string href)
    {
        href = href.Trim().Replace('\\', '/');
        var cut = href.IndexOfAny(['?', '#']);
        if (cut >= 0)
            href = href[..cut];
        if (href.Length == 0
            || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || href.Contains("://", StringComparison.Ordinal))
            return null;
        if (href.StartsWith("~/", StringComparison.Ordinal))
            href = href[2..];
        else if (href.StartsWith('/'))
            href = href[1..];
        if (href.Split('/').Any(part => part is ".." or "."))
            return null;

        var root = Path.GetFullPath(projectDir);
        var relative = href.Replace('/', Path.DirectorySeparatorChar);
        foreach (var baseDir in new[] { Path.Combine(root, "wwwroot"), root })
        {
            var full = Path.GetFullPath(Path.Combine(baseDir, relative));
            var under = full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!under)
                continue;
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
