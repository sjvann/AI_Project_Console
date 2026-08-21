using System.Text;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record CommitContext(
    IReadOnlyList<GitChange> Changes,
    string Stat,
    string Patch,
    IReadOnlyList<string> RecentSubjects);

public sealed record CommitSuggestion(string Message, string Source);

public static class CommitMessageSuggester
{
    private const int MaxPatchChars = 6_000;
    private static readonly Regex FenceRe = new(@"^```(?:\w+)?\s*|\s*```$", RegexOptions.Compiled);
    private static readonly string[] AgentNames = ["agent", "cursor-agent"];

    public static string? ResolveAgentCli()
    {
        foreach (var name in AgentNames)
        {
            var found = CliUtil.FindOnPath(name);
            if (!string.IsNullOrEmpty(found))
                return found;
        }
        return FindWellKnownAgentCli();
    }

    private static string? FindWellKnownAgentCli()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
        foreach (var name in AgentNames)
        {
            var cmd = Path.Combine(local, "cursor-agent", name + ".cmd");
            if (File.Exists(cmd))
                return cmd;
        }
        return null;
    }

    public static async Task<CommitContext> CollectAsync(string root, IReadOnlyList<GitChange> changes)
    {
        var quote = new[] { "-c", "core.quotepath=false" };
        var (_, stat) = await CliUtil.RunAsync("git", [.. quote, "diff", "--stat", "HEAD"], root).ConfigureAwait(false);
        var (_, patch) = await CliUtil.RunAsync("git", [.. quote, "diff", "HEAD"], root, 60_000).ConfigureAwait(false);
        if (patch.Length > MaxPatchChars)
            patch = patch[..MaxPatchChars] + "\n…（diff 過長，已截斷）";
        var (_, log) = await CliUtil.RunAsync("git", ["log", "-8", "--pretty=format:%s"], root).ConfigureAwait(false);
        var recent = string.IsNullOrWhiteSpace(log)
            ? Array.Empty<string>()
            : log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CommitContext(changes, stat.Trim(), patch.Trim(), recent);
    }

    public static async Task<CommitSuggestion> SuggestAsync(string root, IReadOnlyList<GitChange> changes)
    {
        var ctx = await CollectAsync(root, changes).ConfigureAwait(false);
        var cli = ResolveAgentCli();
        if (cli is not null)
        {
            try
            {
                var text = await RunAgentAsync(cli, root, BuildPrompt(ctx)).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                    return new CommitSuggestion(CleanMessage(text), "cursor");
            }
            catch
            {
                // 改用本機草稿
            }
        }
        return new CommitSuggestion(DraftFromContext(ctx), "local");
    }

    public static string BuildPrompt(CommitContext ctx)
    {
        var files = ctx.Changes.Count == 0
            ? "（無檔案清單）"
            : string.Join('\n', ctx.Changes.Select(c => "- " + c.Display()));
        var recent = ctx.RecentSubjects.Count == 0
            ? "（尚無）"
            : string.Join('\n', ctx.RecentSubjects.Select(s => "- " + s));
        return
            "請依下列 git 變更撰寫提交說明。只輸出說明本身，不要前言、不要引號、不要 markdown 標題。\n"
            + "使用繁體中文。第一行是一句話摘要（約 20–50 字），需要時再加空行與最多 4 行「- 」重點。\n"
            + "說明「為什麼／做了什麼」，不要只列檔名。不要修改任何檔案。\n\n"
            + $"變更檔案：\n{files}\n\n"
            + $"diff --stat：\n{(string.IsNullOrEmpty(ctx.Stat) ? "（無，可能多為未追蹤檔）" : ctx.Stat)}\n\n"
            + $"近期提交標題：\n{recent}\n\n"
            + $"diff 摘要：\n```diff\n{(string.IsNullOrEmpty(ctx.Patch) ? "（無已追蹤檔 diff）" : ctx.Patch)}\n```\n";
    }

    public static string DraftFromContext(CommitContext ctx)
    {
        var changes = ctx.Changes;
        if (changes.Count == 0)
            return "更新專案檔案";

        var groups = new Dictionary<string, List<GitChange>>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes)
        {
            var key = Classify(change.Path);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(change);
        }

        var added = changes.Count(c => c.Code.Contains('A') || c.Code.Contains('?'));
        var deleted = changes.Count(c => c.Code.Contains('D'));
        var renamed = changes.Count(c => c.Code.Contains('R'));
        var primary = groups.OrderByDescending(g => g.Value.Count).First().Key;
        var title = TitleFor(primary, changes.Count, added, deleted, renamed, groups.Count);

        if (changes.Count == 1)
        {
            var one = changes[0];
            var name = Path.GetFileName(one.Path.Replace('\\', '/'));
            var verb = GitChange.Describe(one.Code);
            return $"{verb} {name}";
        }

        var bullets = new List<string>();
        foreach (var (key, items) in groups.OrderByDescending(g => g.Value.Count))
        {
            var sample = string.Join("、", items.Take(3).Select(i => ShortName(i.Path)));
            var more = items.Count > 3 ? $" 等 {items.Count} 項" : "";
            bullets.Add($"- {LabelOf(key)}：{sample}{more}");
            if (bullets.Count >= 4)
                break;
        }

        return bullets.Count == 0 ? title : title + "\n\n" + string.Join('\n', bullets);
    }

    public static string CleanMessage(string raw)
    {
        var text = (raw ?? "").Replace("\r\n", "\n").Trim();
        text = FenceRe.Replace(text, "").Trim();
        foreach (var prefix in new[] { "提交說明：", "提交說明:", "Commit message:", "Commit message：" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                text = text[prefix.Length..].Trim();
        }
        if (text.Length > 800)
            text = text[..800].TrimEnd() + "…";
        return text;
    }

    public static string UnescapeGitPath(string path)
    {
        var text = (path ?? "").Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            text = text[1..^1];
        if (!text.Contains('\\'))
            return text;

        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 3 < text.Length
                && IsOctal(text[i + 1]) && IsOctal(text[i + 2]) && IsOctal(text[i + 3]))
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 3), 8));
                i += 3;
                continue;
            }
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(UnescapeChar(text[i + 1]).ToString()));
                i++;
                continue;
            }
            bytes.AddRange(Encoding.UTF8.GetBytes(text[i].ToString()));
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static async Task<string> RunAgentAsync(string cli, string root, string prompt)
    {
        var args = new[] { "-p", prompt, "--mode", "ask", "--output-format", "text" };
        var (code, output) = await CliUtil.RunAsync(cli, args, root, 90_000).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "Cursor Agent 沒有回傳說明。" : output);
        return output;
    }

    private static string Classify(string path)
    {
        var norm = path.Replace('\\', '/');
        var name = Path.GetFileName(norm);
        var ext = Path.GetExtension(norm);
        if (norm.Contains("/Requirement", StringComparison.OrdinalIgnoreCase)
            || norm.StartsWith("Requirement", StringComparison.OrdinalIgnoreCase)
            || ext is ".md" or ".adoc" or ".rst")
            return "docs";
        if (name is ".gitignore" or ".gitattributes" or "ai-project.json"
            || ext is ".json" or ".yml" or ".yaml" or ".config" or ".props" or ".targets")
            return "config";
        if (norm.Contains("/test", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Test", StringComparison.OrdinalIgnoreCase))
            return "tests";
        if (ext is ".cs" or ".ts" or ".js" or ".py" or ".fs" or ".vb")
            return "code";
        return "other";
    }

    private static string LabelOf(string key) => key switch
    {
        "docs" => "文件／需求",
        "config" => "設定",
        "tests" => "測試",
        "code" => "程式",
        _ => "其他",
    };

    private static string TitleFor(string primary, int total, int added, int deleted, int renamed, int groupCount)
    {
        if (renamed > 0 && renamed == total)
            return "重新整理檔案路徑";
        if (deleted > 0 && deleted == total)
            return "移除不再使用的檔案";
        if (added > 0 && added == total)
        {
            return primary switch
            {
                "docs" => "新增需求／說明文件",
                "config" => "新增專案設定",
                "tests" => "新增測試",
                "code" => "新增程式碼",
                _ => $"新增 {total} 個檔案",
            };
        }
        if (groupCount == 1)
        {
            return primary switch
            {
                "docs" => "更新需求／說明文件",
                "config" => "調整專案設定",
                "tests" => "更新測試",
                "code" => "更新程式碼",
                _ => "更新專案檔案",
            };
        }
        return primary switch
        {
            "docs" => "更新文件並調整相關設定",
            "config" => "調整設定並更新相關檔案",
            "code" => "更新程式與相關檔案",
            _ => "更新專案變更",
        };
    }

    private static string ShortName(string path)
    {
        var norm = path.Replace('\\', '/');
        var name = Path.GetFileName(norm);
        return string.IsNullOrEmpty(name) ? norm : name;
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';

    private static char UnescapeChar(char c) => c switch
    {
        'n' => '\n',
        't' => '\t',
        'r' => '\r',
        '"' => '"',
        '\\' => '\\',
        _ => c,
    };
}
