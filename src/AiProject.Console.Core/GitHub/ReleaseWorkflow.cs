using System.Text;
using System.Text.RegularExpressions;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 控制台建立的 tag 一律帶 v。倉庫若只在「數字開頭的 tag」上打包，
/// 這次 tag 推送不會跑到發行工作流程。改由遠端預設分支上的 workflow_dispatch 來打包，
/// 使用者不必先把該工作流程拉進本次 tag。
/// </summary>
public static class ReleaseWorkflow
{
    public sealed record Dispatch(string File, string Ref, string InputName, string Tag);

    public static string CanonicalTag(string? tag)
    {
        var raw = (tag ?? "").Trim();
        if (ReleaseVersion.TryParse(raw, out var parsed))
            return string.IsNullOrEmpty(parsed.Prefix) ? "v" + parsed.ToTag() : parsed.ToTag();
        return raw;
    }

    public static bool CanDispatch(string? yaml) =>
        HasDispatchTagInput(yaml) && PublishesRelease(yaml);

    public static bool PublishesRelease(string? yaml) =>
        !string.IsNullOrWhiteSpace(yaml)
        && (yaml.Contains("action-gh-release", StringComparison.OrdinalIgnoreCase)
            || yaml.Contains("upload-release-asset", StringComparison.OrdinalIgnoreCase));

    public static bool PushWouldRun(string? yaml, string? tag)
    {
        var name = (tag ?? "").Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(yaml))
            return false;
        return PushTagFilters(yaml).Any(pattern => GlobMatches(pattern, name));
    }

    /// <summary>
    /// 被打 tag 的那個 commit 若自己會跑發行工作流程，就不必再派一次。
    /// 否則用預設分支上的同一支 workflow（遠端最新定義）以 tag 輸入啟動。
    /// </summary>
    public static Dispatch? Plan(string fileName, string? yamlAtTag, string? yamlLocal, string? tag, string? defaultBranch)
    {
        var canonical = CanonicalTag(tag);
        var file = Path.GetFileName(fileName ?? "");
        if (string.IsNullOrEmpty(canonical) || string.IsNullOrEmpty(file))
            return null;
        var capable = CanDispatch(yamlLocal) ? yamlLocal : CanDispatch(yamlAtTag) ? yamlAtTag : null;
        if (capable is null || PushWouldRun(yamlAtTag, canonical))
            return null;
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim();
        return new Dispatch(file, branch, "tag", canonical);
    }

    public static List<string> WorkflowRunArgs(Dispatch plan) =>
    [
        "workflow", "run", plan.File,
        "--ref", plan.Ref,
        "-f", plan.InputName + "=" + plan.Tag,
    ];

    public static string? ConfirmNoteForYaml(string? yaml, string? tag)
    {
        if (!CanDispatch(yaml))
            return null;
        var canonical = CanonicalTag(tag);
        if (PushWouldRun(yaml, canonical))
            return "這個 tag 會啟動倉庫的發行工作流程，由它上傳各平台安裝檔。";
        return "發行後會用遠端預設分支上的發行工作流程，為 " + canonical + " 打包並上傳各平台安裝檔。不必先把該工作流程拉進本次 tag。";
    }

    public static string? ConfirmNote(string root, string? tag)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var dir = Path.Combine(root, ".github", "workflows");
        if (!Directory.Exists(dir))
            return null;
        foreach (var file in WorkflowFiles(dir))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }
            var note = ConfirmNoteForYaml(text, tag);
            if (!string.IsNullOrEmpty(note))
                return note;
        }
        return null;
    }

    public static IEnumerable<string> WorkflowFiles(string dir)
    {
        if (!Directory.Exists(dir))
            yield break;
        foreach (var pattern in new[] { "*.yml", "*.yaml" })
        {
            foreach (var file in Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                yield return file;
        }
    }

    public static bool HasDispatchTagInput(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return false;
        var inDispatch = false;
        var inInputs = false;
        var dispatchIndent = 0;
        var inputsIndent = 0;
        foreach (var (indent, text) in Lines(yaml))
        {
            if (text.StartsWith("workflow_dispatch:", StringComparison.Ordinal))
            {
                inDispatch = true;
                dispatchIndent = indent;
                inInputs = false;
                continue;
            }
            if (inDispatch && indent <= dispatchIndent)
            {
                inDispatch = false;
                inInputs = false;
            }
            if (!inDispatch)
                continue;
            if (text.StartsWith("inputs:", StringComparison.Ordinal))
            {
                inInputs = true;
                inputsIndent = indent;
                continue;
            }
            if (inInputs && indent <= inputsIndent)
                inInputs = false;
            if (inInputs && (text == "tag:" || text.StartsWith("tag:", StringComparison.Ordinal)))
                return true;
        }
        return false;
    }

    public static IReadOnlyList<string> PushTagFilters(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return [];
        var inOn = false;
        var inPush = false;
        var inTags = false;
        var onIndent = 0;
        var pushIndent = 0;
        var tagsIndent = 0;
        var filters = new List<string>();
        foreach (var (indent, text) in Lines(yaml))
        {
            if (text.StartsWith("on:", StringComparison.Ordinal))
            {
                inOn = true;
                onIndent = indent;
                inPush = false;
                inTags = false;
                continue;
            }
            if (inOn && indent <= onIndent)
            {
                inOn = false;
                inPush = false;
                inTags = false;
            }
            if (!inOn)
                continue;
            if (text.StartsWith("push:", StringComparison.Ordinal))
            {
                inPush = true;
                pushIndent = indent;
                inTags = false;
                continue;
            }
            if (inPush && indent <= pushIndent)
            {
                inPush = false;
                inTags = false;
            }
            if (!inPush)
                continue;
            if (text.StartsWith("tags:", StringComparison.Ordinal))
            {
                inTags = true;
                tagsIndent = indent;
                continue;
            }
            if (inTags && indent <= tagsIndent)
                inTags = false;
            if (!inTags || !text.StartsWith("- ", StringComparison.Ordinal))
                continue;
            var pattern = Unquote(text[2..].Trim());
            if (!string.IsNullOrEmpty(pattern))
                filters.Add(pattern);
        }
        return filters;
    }

    public static bool GlobMatches(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern) || text is null)
            return false;
        var regex = "^" + GlobToRegex(pattern) + "$";
        return Regex.IsMatch(text, regex, RegexOptions.CultureInvariant);
    }

    static string GlobToRegex(string pattern)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                sb.Append("[^/]*");
                continue;
            }
            if (c == '?')
            {
                sb.Append("[^/]");
                continue;
            }
            if (c == '[')
            {
                var end = pattern.IndexOf(']', i + 1);
                if (end > i)
                {
                    sb.Append(pattern.AsSpan(i, end - i + 1));
                    i = end;
                    continue;
                }
            }
            sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }

    static string Unquote(string text)
    {
        if (text.Length >= 2
            && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
            return text[1..^1];
        return text;
    }

    static IEnumerable<(int Indent, string Text)> Lines(string yaml)
    {
        foreach (var raw in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var indent = 0;
            while (indent < raw.Length && raw[indent] == ' ')
                indent++;
            var text = raw[indent..].Trim();
            if (text.Length == 0 || text.StartsWith('#'))
                continue;
            yield return (indent, text);
        }
    }
}
