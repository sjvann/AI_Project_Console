namespace AiProject.Console.Core.Agents;

/// <summary>
/// Cursor／VS Code 視窗標題與工作區資料夾的對應（一份 IDE、每個方案一個視窗）。
/// </summary>
public static class WorkspaceWindow
{
    public static string? FolderName(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;
        try
        {
            var full = Path.GetFullPath(workspaceRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(full);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    public static bool TitleMatches(string? title, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(workspaceRoot))
            return false;
        string full;
        try { full = Path.GetFullPath(workspaceRoot); }
        catch { return false; }
        var name = FolderName(full);
        if (name is null)
            return false;

        if (title.Contains(full, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var part in TitleSegments(title))
        {
            if (part.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
            if (part.Equals(full, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    internal static IEnumerable<string> TitleSegments(string title)
    {
        foreach (var raw in title.Split(" - ", StringSplitOptions.None))
        {
            var t = raw.Trim().TrimStart('\u25CF', '*', ' ');
            if (t.Length > 0)
                yield return t;
        }
    }
}
