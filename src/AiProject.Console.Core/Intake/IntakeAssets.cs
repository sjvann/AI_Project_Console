namespace AiProject.Console.Core.Intake;

public static class IntakeAssets
{
    public const string RelDir = "docs/product/intake-assets";
    public const int MaxFilesPerKind = 8;
    public const long MaxBytes = 8 * 1024 * 1024;

    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    public static string FolderRel(string intakeId) =>
        RelDir + "/" + SanitizeId(intakeId);

    public static string CopyIn(string root, string intakeId, string source, string kind)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(intakeId))
            throw new InvalidOperationException("沒有工作區或進件編號。");
        if (!File.Exists(source))
            throw new InvalidOperationException("找不到圖片檔。");
        var ext = Path.GetExtension(source);
        if (!ImageExts.Contains(ext))
            throw new InvalidOperationException("只接受 PNG／JPG／GIF／WebP／BMP。");
        var len = new FileInfo(source).Length;
        if (len <= 0 || len > MaxBytes)
            throw new InvalidOperationException("圖片需小於 8 MB。");
        var dir = Path.Combine(Path.GetFullPath(root), RelDir.Replace('/', Path.DirectorySeparatorChar), SanitizeId(intakeId));
        Directory.CreateDirectory(dir);
        var prefix = kind == "crop" ? "crop" : "sketch";
        var name = $"{prefix}-{DateTime.Now:HHmmss}-{Guid.NewGuid().ToString("N")[..4]}{ext.ToLowerInvariant()}";
        var dest = Path.Combine(dir, name);
        File.Copy(source, dest, overwrite: false);
        return FolderRel(intakeId) + "/" + name;
    }

    public static void DeleteFile(string root, string rel)
    {
        if (!TryResolve(root, rel, out var full) || !File.Exists(full))
            return;
        try
        {
            File.Delete(full);
        }
        catch (IOException)
        {
        }
    }

    public static void DeleteFolder(string root, string intakeId)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(intakeId))
            return;
        var dir = Path.Combine(Path.GetFullPath(root), RelDir.Replace('/', Path.DirectorySeparatorChar), SanitizeId(intakeId));
        var allowed = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), RelDir.Replace('/', Path.DirectorySeparatorChar)));
        if (!dir.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(dir, allowed, StringComparison.OrdinalIgnoreCase))
            return;
        if (!Directory.Exists(dir))
            return;
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    public static string? TryDataUrl(string root, string rel)
    {
        if (!TryResolve(root, rel, out var full) || !File.Exists(full))
            return null;
        var ext = Path.GetExtension(full).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => null,
        };
        if (mime is null)
            return null;
        try
        {
            return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(full))}";
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static bool TryResolve(string root, string rel, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(rel))
            return false;
        if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            return false;
        var candidate = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), rel.Replace('/', Path.DirectorySeparatorChar)));
        var allowed = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), RelDir.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return false;
        full = candidate;
        return true;
    }

    static string SanitizeId(string intakeId)
    {
        var chars = intakeId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrEmpty(id) ? "intake" : id;
    }
}
