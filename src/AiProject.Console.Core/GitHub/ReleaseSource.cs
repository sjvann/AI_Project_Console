using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// Release 原始碼壓縮檔。GitHub 仍會在頁面底部顯示自動產生的 Source code
/// zip／tar.gz（平台無法關閉）；這裡只控制我們自己上傳的資產。
/// </summary>
public static class ReleaseSource
{
    static readonly Regex SourceName = new(
        @"(^|[.\-_])src\.(zip|tar\.gz|tgz)$|(^|[-_ ])source([- ]code)?\.(zip|tar\.gz|tgz)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsSourceAsset(string? path)
    {
        var name = Path.GetFileName(path ?? "");
        return !string.IsNullOrWhiteSpace(name) && SourceName.IsMatch(name.Trim());
    }

    public static IReadOnlyList<string> FilterOutSource(IEnumerable<string>? paths) =>
        (paths ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && !IsSourceAsset(p)).ToList();

    public static IReadOnlyList<string> SelectUploadAssets(IEnumerable<string>? assets, bool includeSource)
    {
        var list = (assets ?? []).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        return includeSource ? list : FilterOutSource(list);
    }

    public static string FileName(string? repo, string tag)
    {
        var name = Sanitize(string.IsNullOrWhiteSpace(repo) ? "source" : repo);
        var ver = ConsoleReleasePack.BareVersion(tag);
        if (string.IsNullOrEmpty(ver))
            ver = "src";
        return $"{name}-{ver}-src.zip";
    }

    public static string PolicyNotes(bool includeSource, string? sourceZipName = null)
    {
        if (includeSource)
        {
            var name = string.IsNullOrWhiteSpace(sourceZipName) ? "*-src.zip" : Path.GetFileName(sourceZipName);
            return $"- **{name}**：原始碼壓縮檔（git archive）。一般使用者請改下載安裝包。";
        }

        return "一般使用者請下載安裝包或你上傳的檔案。GitHub 頁面底部的 Source code (zip／tar.gz) 是倉庫快照，不是安裝檔；平台無法關閉這兩個連結。公開倉本來就能 clone。";
    }

    public static string MergeNotes(string? userNotes, bool includeSource, string? sourceZipName = null)
    {
        var extra = (userNotes ?? "").Trim();
        var policy = PolicyNotes(includeSource, sourceZipName);
        if (includeSource)
        {
            var name = string.IsNullOrWhiteSpace(sourceZipName) ? "" : Path.GetFileName(sourceZipName);
            if (!string.IsNullOrEmpty(name) && extra.Contains(name, StringComparison.OrdinalIgnoreCase))
                return extra;
            if (extra.Contains("-src.zip", StringComparison.OrdinalIgnoreCase))
                return extra;
        }
        else if (extra.Contains("Source code", StringComparison.OrdinalIgnoreCase))
            return extra;
        return string.IsNullOrEmpty(extra) ? policy : extra + "\n\n" + policy;
    }

    public static async Task<string> ArchiveAsync(
        string root,
        string tag,
        string destPath,
        string? committish = null,
        IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new InvalidOperationException("找不到專案目錄，無法打包原始碼。");
        var dest = Path.GetFullPath(destPath);
        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(dest))
            File.Delete(dest);

        var target = string.IsNullOrWhiteSpace(committish) ? "HEAD" : committish.Trim();
        var prefix = Sanitize(Path.GetFileNameWithoutExtension(dest));
        progress?.Report(ReleaseRunState.StageLine("upload", "正在打包原始碼…"));
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
            "git",
            ["archive", "--format=zip", "--prefix=" + prefix + "/", "-o", dest, target],
            cwd: Path.GetFullPath(root),
            timeoutMs: 180_000).ConfigureAwait(false);
        if (code != 0 || !File.Exists(dest))
        {
            var err = string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            throw new InvalidOperationException(string.IsNullOrEmpty(err)
                ? $"打包原始碼失敗（git archive，{tag}）。"
                : err);
        }

        return dest;
    }

    static string Sanitize(string text)
    {
        var t = (text ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            t = t.Replace(c, '-');
        t = t.Replace(' ', '-');
        return string.IsNullOrEmpty(t) ? "source" : t;
    }
}
