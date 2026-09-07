using System.Net;
using System.Text.RegularExpressions;
using AiProject.Console.Core.GitHub;
using Markdig;

namespace AiProject.Console.App.Services;

public static class DocsMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly Regex TagRe = new(
        @"</?(script|style|iframe|object|embed|link|meta|form|base)(\s[^>]*)?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EventAttrRe = new(
        @"\s+on[a-z]+\s*=\s*(['""])[\s\S]*?\1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex JsUrlRe = new(
        @"\s+(href|src)\s*=\s*(['""])\s*javascript:[\s\S]*?\2",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string ToSafeHtml(string? markdown)
    {
        var html = Markdown.ToHtml(markdown ?? "", Pipeline);
        return Sanitize(html);
    }

    /// <summary>Issue／討論用：先把 GitHub 的 HTML 圖片轉成 Markdown，再換成已下載的圖，再做安全 HTML。</summary>
    public static string ToIssueHtml(string? markdown, IReadOnlyDictionary<string, string>? imageMap = null) =>
        ToSafeHtml(IssueMarkdown.ApplyImageMap(IssueMarkdown.NormalizeForDisplay(markdown), imageMap));

    public static string Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var text = TagRe.Replace(html, "");
        text = EventAttrRe.Replace(text, "");
        text = JsUrlRe.Replace(text, "");
        return text;
    }

    public static string EncodePlain(string? text) => WebUtility.HtmlEncode(text ?? "");
}
