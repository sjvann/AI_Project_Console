namespace AiProject.Console.Core.WorkHours;

public static class WorkHoursProject
{
    public const string UnallocatedKey = "";
    public const string UnallocatedName = "未掛專案";

    public static string Key(string? slug, string? root)
    {
        var s = (slug ?? "").Trim();
        if (!string.IsNullOrEmpty(s))
            return "gh:" + s.ToLowerInvariant();
        var r = NormalizeRoot(root);
        return string.IsNullOrEmpty(r) ? UnallocatedKey : "path:" + r.ToLowerInvariant();
    }

    public static string Key(in WorkSession session) => Key(session.GithubSlug, session.ProjectRoot);

    public static string DisplayName(string? name, string? root)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        var r = (root ?? "").Trim().TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(r))
            return UnallocatedName;
        var leaf = Path.GetFileName(r);
        return string.IsNullOrEmpty(leaf) ? r : leaf;
    }

    public static string NormalizeRoot(string? root)
    {
        var value = (root ?? "").Trim();
        if (string.IsNullOrEmpty(value))
            return "";
        try
        {
            return Path.GetFullPath(value);
        }
        catch (ArgumentException)
        {
            return value;
        }
        catch (NotSupportedException)
        {
            return value;
        }
        catch (PathTooLongException)
        {
            return value;
        }
    }

    public static bool SameRoot(string? a, string? b) =>
        string.Equals(NormalizeRoot(a), NormalizeRoot(b), StringComparison.OrdinalIgnoreCase);

    public static bool SameProject(in WorkSession session, string? root, string? slug)
    {
        var s = (slug ?? "").Trim();
        if (!string.IsNullOrEmpty(s) && !string.IsNullOrWhiteSpace(session.GithubSlug)
            && string.Equals(session.GithubSlug, s, StringComparison.OrdinalIgnoreCase))
            return true;
        return SameRoot(session.ProjectRoot, root);
    }

    public static string FileSlug(string personKey)
    {
        var raw = (personKey ?? "").Trim();
        if (raw.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            raw = raw[7..];
        else if (raw.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            raw = "local-" + raw[6..];
        var chars = raw.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        var safe = new string(chars).Trim('-');
        return string.IsNullOrEmpty(safe) ? "user" : safe.ToLowerInvariant();
    }
}
