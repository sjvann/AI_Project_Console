namespace AiProject.Console.Core.Intake;

public static class IntakeDesignFiles
{
    public const string DefaultDir = "docs/product/design";
    public const int MaxFiles = 8;
    public const long MaxBytes = 20 * 1024 * 1024;

    static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".pdf", ".doc", ".docx", ".rtf",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".xlsx", ".pptx", ".csv",
    };

    public static string NormalizeDir(string? dir)
    {
        var raw = (dir ?? "").Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(raw))
            return DefaultDir;
        if (raw.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(raw.Replace('/', Path.DirectorySeparatorChar)))
            throw new InvalidOperationException("目錄必須在專案的 docs/ 底下。");
        if (!raw.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) && !raw.Equals("docs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("目錄必須在專案的 docs/ 底下。");
        return raw;
    }

    public static string CopyIn(string root, string dir, string intakeId, string source)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(intakeId))
            throw new InvalidOperationException("沒有工作區或進件編號。");
        if (!File.Exists(source))
            throw new InvalidOperationException("找不到檔案。");
        var ext = Path.GetExtension(source);
        if (!AllowedExt.Contains(ext))
            throw new InvalidOperationException("只接受 Markdown／PDF／Office／圖片／CSV。");
        var len = new FileInfo(source).Length;
        if (len <= 0 || len > MaxBytes)
            throw new InvalidOperationException("單一檔案需小於 20 MB。");
        var folder = NormalizeDir(dir);
        var id = SanitizeId(intakeId);
        var destDir = Path.Combine(Path.GetFullPath(root), folder.Replace('/', Path.DirectorySeparatorChar), id);
        Directory.CreateDirectory(destDir);
        var name = UniqueName(destDir, SanitizeFileName(Path.GetFileName(source)));
        var dest = Path.Combine(destDir, name);
        File.Copy(source, dest, overwrite: false);
        return folder + "/" + id + "/" + name;
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

    public static void DeleteIntakeFolder(string root, string dir, string intakeId)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(intakeId))
            return;
        string folder;
        try
        {
            folder = NormalizeDir(dir);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        var destDir = Path.Combine(Path.GetFullPath(root), folder.Replace('/', Path.DirectorySeparatorChar), SanitizeId(intakeId));
        if (!TryResolve(root, folder + "/" + SanitizeId(intakeId), out _))
            return;
        if (!Directory.Exists(destDir))
            return;
        try
        {
            Directory.Delete(destDir, true);
        }
        catch (IOException)
        {
        }
    }

    public static bool TryResolve(string root, string rel, out string full)
    {
        full = "";
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(rel))
            return false;
        if (rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            return false;
        var norm = rel.Replace('\\', '/').Trim('/');
        if (!norm.StartsWith("docs/", StringComparison.OrdinalIgnoreCase) && !norm.Equals("docs", StringComparison.OrdinalIgnoreCase))
            return false;
        var candidate = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), norm.Replace('/', Path.DirectorySeparatorChar)));
        var docs = Path.GetFullPath(Path.Combine(Path.GetFullPath(root), "docs"));
        if (!candidate.StartsWith(docs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, docs, StringComparison.OrdinalIgnoreCase))
            return false;
        full = candidate;
        return true;
    }

    public static string BlobUrl(string webUrl, string branch, string rel)
    {
        var baseUrl = (webUrl ?? "").Trim().TrimEnd('/');
        var br = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
        var parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return baseUrl + "/blob/" + Uri.EscapeDataString(br) + "/" + string.Join('/', parts);
    }

    static string UniqueName(string destDir, string name)
    {
        if (string.IsNullOrEmpty(name))
            name = "file.bin";
        var dest = Path.Combine(destDir, name);
        if (!File.Exists(dest))
            return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; i < 100; i++)
        {
            var cand = $"{stem}-{i}{ext}";
            if (!File.Exists(Path.Combine(destDir, cand)))
                return cand;
        }
        return $"{stem}-{Guid.NewGuid().ToString("N")[..4]}{ext}";
    }

    static string SanitizeFileName(string name)
    {
        var chars = name.Trim().Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ' || c >= 0x4E00
                ? c
                : '-').ToArray();
        var cleaned = new string(chars).Trim().Trim('.');
        return string.IsNullOrEmpty(cleaned) ? "file.bin" : cleaned;
    }

    static string SanitizeId(string intakeId)
    {
        var chars = intakeId.Trim().Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var id = new string(chars).Trim('-');
        return string.IsNullOrEmpty(id) ? "intake" : id;
    }
}
