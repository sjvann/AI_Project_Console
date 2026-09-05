using System.Net;
using System.Text.RegularExpressions;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// GitHub Issue 內文常混有 HTML <c>img</c>（貼上截圖），轉成 Markdown 後才能在控制台顯示。
/// </summary>
public static class IssueMarkdown
{
    static readonly Regex LinkedImgRe = new(
        @"<a\b[^>]*>\s*<img\b[^>]*?/?>\s*</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    static readonly Regex ImgRe = new(
        @"<img\b[^>]*?/?>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    static readonly Regex AnchorRe = new(
        @"<a\s+[^>]*href\s*=\s*(['""])(?<href>.*?)\1[^>]*>(?<inner>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    static readonly Regex AttrRe = new(
        @"\b(?<name>src|alt|href)\s*=\s*(?:(['""])(?<q>.*?)\1|(?<u>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    static readonly Regex MarkdownImgRe = new(
        @"!\[[^\]]*\]\((?<url>https?://[^)\s]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>把 HTML 圖片／連結改成 Markdown，其餘內文原樣保留。</summary>
    public static string NormalizeForDisplay(string? markdown)
    {
        var text = markdown ?? "";
        if (text.Length == 0)
            return "";
        text = LinkedImgRe.Replace(text, m => ImgToMarkdown(m.Value));
        text = ImgRe.Replace(text, m => ImgToMarkdown(m.Value));
        text = AnchorRe.Replace(text, AnchorToMarkdown);
        return text;
    }

    public static IReadOnlyList<string> FindHttpImageUrls(string? markdown)
    {
        var text = NormalizeForDisplay(markdown);
        if (text.Length == 0)
            return [];
        var urls = new List<string>();
        foreach (Match m in MarkdownImgRe.Matches(text))
        {
            var url = m.Groups["url"].Value.Trim();
            if (IsSafeHttpUrl(url))
                urls.Add(url);
        }
        return urls.Distinct(StringComparer.Ordinal).ToList();
    }

    public static string ApplyImageMap(string? markdown, IReadOnlyDictionary<string, string>? map)
    {
        var text = markdown ?? "";
        if (map is null || map.Count == 0)
            return text;
        foreach (var (from, to) in map)
        {
            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
                text = text.Replace(from, to, StringComparison.Ordinal);
        }
        return text;
    }

    public static bool IsTrustedImageHost(string? url, string? extraHost = null)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return false;
        var host = uri.Host.ToLowerInvariant();
        if (host == "github.com"
            || host.EndsWith(".github.com", StringComparison.Ordinal)
            || host == "githubusercontent.com"
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(extraHost))
            return false;
        var extra = GitHost.Normalize(extraHost);
        return host == extra || host.EndsWith("." + extra, StringComparison.Ordinal);
    }

    static string ImgToMarkdown(string tag)
    {
        var img = ImgRe.Match(tag);
        var src = Attr(img.Success ? img.Value : tag, "src");
        var alt = Attr(img.Success ? img.Value : tag, "alt");
        if (!IsSafeHttpUrl(src))
            return "";
        alt = (alt ?? "").Replace("]", "", StringComparison.Ordinal);
        return $"![{alt}]({src})";
    }

    static string AnchorToMarkdown(Match m)
    {
        var inner = m.Groups["inner"].Value.Trim();
        var href = WebUtility.HtmlDecode(m.Groups["href"].Value.Trim());
        if (inner.StartsWith("![", StringComparison.Ordinal))
            return inner;
        if (!IsSafeHttpUrl(href))
            return inner;
        var label = inner.Replace("]", "", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(label))
            return href;
        return $"[{label}]({href})";
    }

    static string Attr(string tag, string name)
    {
        foreach (Match m in AttrRe.Matches(tag))
        {
            if (!string.Equals(m.Groups["name"].Value, name, StringComparison.OrdinalIgnoreCase))
                continue;
            var quoted = m.Groups["q"].Value;
            var unquoted = m.Groups["u"].Value;
            return WebUtility.HtmlDecode(quoted.Length > 0 ? quoted : unquoted).Trim();
        }
        return "";
    }

    public static bool IsSafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
