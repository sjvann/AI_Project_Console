namespace AiProject.Console.Core;

public sealed record InfoField(
    string Label,
    string Value,
    string? Badge = null,
    string? Tone = null,
    string? Detail = null);

public sealed record InfoSection(
    string Id,
    string Title,
    IReadOnlyList<InfoField> Fields,
    string? Note = null,
    string? Badge = null,
    string? Tone = null);

public sealed record InfoReport(
    string Title,
    string Hint,
    string Headline,
    string HeadlineDetail,
    string Tone,
    IReadOnlyList<InfoSection> Sections,
    string Text,
    string? PrimaryAction = null,
    string? PrimaryLabel = null);

public sealed record JobResultView(string Title, string Tone, string Summary, string? Detail)
{
    public bool HasDetail =>
        !string.IsNullOrWhiteSpace(Detail)
        && !string.Equals(Detail.Trim(), Summary.Trim(), StringComparison.Ordinal);

    public static bool IsDense(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        var t = message.Trim();
        if (t.IndexOfAny(['\r', '\n']) >= 0)
            return true;
        return t.Length > 160;
    }

    public static string CleanTitle(string title)
    {
        var t = (title ?? "").Trim();
        return t.TrimEnd('…', ' ').TrimEnd();
    }
}
