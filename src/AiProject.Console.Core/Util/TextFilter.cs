namespace AiProject.Console.Core.Util;

public static class TextFilter
{
    public static string Apply(string text, string? query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(query))
            return text;
        var q = query.Trim();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        return string.Join('\n', lines.Where(l => l.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }
}
