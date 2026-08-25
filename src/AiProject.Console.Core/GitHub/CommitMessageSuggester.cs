using System.Text;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record CommitContext(
    IReadOnlyList<GitChange> Changes,
    string Stat,
    string Patch,
    IReadOnlyList<string> RecentSubjects);

public sealed record CommitSuggestion(string Message, string Source, string Hint);

public static class CommitMessageSuggester
{
    private const int MaxPatchChars = 6_000;
    private static readonly Regex FenceRe = new(@"^```(?:\w+)?\s*|\s*```$", RegexOptions.Compiled);
    private static readonly Regex AgentVersionDirRe = new(
        @"^(\d{4})\.(\d{1,2})\.(\d{1,2})(?:-\d{2}-\d{2}-\d{2})?-[a-f0-9]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
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

    public static string DoctorLine()
    {
        var cli = ResolveAgentCli();
        if (cli is null)
            return "Cursor Agent CLI: 缺少（選用；提交對話框的「AI 建議」會改依 diff 產生草稿）";
        var login = ProbeAgentLogin(cli);
        return login is null
            ? "Cursor Agent CLI: OK — " + cli
            : "Cursor Agent CLI: 已安裝但未登入 — " + cli + "（請在終端機執行 agent login）";
    }

    public static string LocalDraftHint(string? agentError)
    {
        if (string.IsNullOrWhiteSpace(agentError))
            return "已依變更內容產生建議（未偵測到 Cursor Agent CLI）。可再修改後提交。";
        return "已依變更內容產生建議（" + SummarizeAgentError(agentError) + "）。可再修改後提交。";
    }

    public static string SummarizeAgentError(string error)
    {
        if (LooksLikeAuthError(error))
            return "Cursor Agent 尚未登入，請在終端機執行 agent login";
        if (LooksLikeTrustError(error))
            return "Cursor Agent 需要工作區信任；請在該專案目錄執行 agent 並選擇 Trust，或再試一次";
        if (LooksLikeTransportError(error))
            return "Cursor Agent 連線失敗（網路或憑證），已改依變更產生草稿";
        if (error.Contains("逾時", StringComparison.Ordinal))
            return "Cursor Agent 回應逾時";
        var first = error.Replace("\r\n", "\n").Split('\n')
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim() ?? error.Trim();
        if (first.Length > 120)
            first = first[..120] + "…";
        return "Cursor Agent 呼叫失敗：" + first;
    }

    public static bool LooksLikeAuthError(string error) =>
        error.Contains("Authentication required", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Not authenticated", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeTrustError(string error) =>
        error.Contains("Workspace Trust Required", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Failed to trust workspace", StringComparison.OrdinalIgnoreCase);

    public static bool LooksLikeTransportError(string error) =>
        error.Contains("self-signed certificate", StringComparison.OrdinalIgnoreCase)
        || error.Contains("certificate in certificate chain", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Connection lost", StringComparison.OrdinalIgnoreCase)
        || error.Contains("RetriableError", StringComparison.OrdinalIgnoreCase)
        || error.Contains("agentn.global.api", StringComparison.OrdinalIgnoreCase);

    private static string? ProbeAgentLogin(string cli)
    {
        try
        {
            var (_, output) = CliUtil.RunAsync(cli, ["status"], timeoutMs: 20_000).GetAwaiter().GetResult();
            return LooksLikeAuthError(output)
                ? "尚未登入"
                : null;
        }
        catch
        {
            return null;
        }
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
                    return new CommitSuggestion(CleanMessage(text), "cursor", "已用 Cursor Agent 產生，可再修改後提交。");
            }
            catch (Exception ex)
            {
                return new CommitSuggestion(DraftFromContext(ctx), "local", LocalDraftHint(ex.Message));
            }
        }
        return new CommitSuggestion(DraftFromContext(ctx), "local", LocalDraftHint(null));
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

    /// <summary>
    /// Agent CLI 常把說明寫到 stdout、診斷寫到 stderr。合併後會變成「建議說明下面跟著錯誤」。
    /// 優先用 stdout；沒有可用說明才看 stderr。
    /// </summary>
    public static string? PickAgentMessage(string stdout, string stderr)
    {
        foreach (var raw in new[] { stdout, stderr })
        {
            var cleaned = CleanMessage(StripAgentDiagnostics(raw));
            if (string.IsNullOrWhiteSpace(cleaned) || LooksLikeAgentFailureOutput(cleaned))
                continue;
            return cleaned;
        }
        return null;
    }

    public static string StripAgentDiagnostics(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var kept = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsAgentNoiseLine(line))
                continue;
            kept.Add(line.TrimEnd());
        }
        return string.Join('\n', kept).Trim();
    }

    internal static bool IsAgentNoiseLine(string line)
    {
        var t = (line ?? "").Trim();
        if (t.Length == 0)
            return false;
        if (t.StartsWith("⚠", StringComparison.Ordinal) || t.StartsWith("⚠️", StringComparison.Ordinal))
            return true;
        if (t.StartsWith('{') && t.Contains("\"type\"", StringComparison.Ordinal))
            return true;
        if (t.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("warn:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("Workspace Trust Required", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Failed to trust workspace", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Authentication required", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Not authenticated", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("Pass --trust", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Pass --yolo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("Connection lost", StringComparison.OrdinalIgnoreCase)
            || t.Contains("reconnecting to", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Retry attempt", StringComparison.OrdinalIgnoreCase)
            || t.Contains("RetriableError", StringComparison.OrdinalIgnoreCase)
            || t.Contains("self-signed certificate", StringComparison.OrdinalIgnoreCase)
            || t.Contains("certificate in certificate chain", StringComparison.OrdinalIgnoreCase)
            || t.Contains("agentn.global.api", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool LooksLikeAgentFailureOutput(string text) =>
        LooksLikeTransportError(text)
        || LooksLikeAuthError(text)
        || LooksLikeTrustError(text);

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

    public static string[] BuildAgentFlags(string root) =>
        ["-p", "--trust", "--mode", "ask", "--output-format", "text", "--workspace", Path.GetFullPath(root)];

    /// <summary>
    /// Windows 的 agent.cmd 會再包一層 cmd → PowerShell；長 prompt 與 diff 裡的 -- 會讓
    /// <c>--trust</c> 沒進 CLI，進而出現 Workspace Trust Required。能解到 node.exe 就直呼。
    /// </summary>
    public static (string FileName, string[] PrefixArgs)? TryUnwrapWindowsAgent(string cli)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(cli))
            return null;
        var ext = Path.GetExtension(cli);
        if (!ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return null;
        string dir;
        try { dir = Path.GetDirectoryName(Path.GetFullPath(cli)) ?? ""; }
        catch { return null; }
        if (dir.Length == 0)
            return null;

        var direct = TryNodeEntry(dir);
        if (direct is not null)
            return direct;

        var versions = Path.Combine(dir, "versions");
        if (!Directory.Exists(versions))
            return null;
        string? bestDir = null;
        var bestKey = -1;
        foreach (var candidate in Directory.EnumerateDirectories(versions))
        {
            var name = Path.GetFileName(candidate);
            var m = AgentVersionDirRe.Match(name);
            if (!m.Success)
                continue;
            var key = int.Parse(m.Groups[1].Value) * 10_000
                + int.Parse(m.Groups[2].Value) * 100
                + int.Parse(m.Groups[3].Value);
            if (key < bestKey)
                continue;
            if (TryNodeEntry(candidate) is null)
                continue;
            bestKey = key;
            bestDir = candidate;
        }
        return bestDir is null ? null : TryNodeEntry(bestDir);
    }

    private static bool IsWindowsBatchWrapper(string cli)
    {
        var ext = Path.GetExtension(cli);
        return ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".bat", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
    }

    private static (string FileName, string[] PrefixArgs)? TryNodeEntry(string dir)
    {
        var node = Path.Combine(dir, "node.exe");
        var index = Path.Combine(dir, "index.js");
        if (!File.Exists(node) || !File.Exists(index))
            return null;
        return (node, [index]);
    }

    private static async Task<string> RunAgentAsync(string cli, string root, string prompt)
    {
        var flags = BuildAgentFlags(root);
        string fileName;
        string[] args;
        string? stdin;
        var unwrapped = TryUnwrapWindowsAgent(cli);
        if (unwrapped is { } u)
        {
            fileName = u.FileName;
            args = [.. u.PrefixArgs, .. flags, prompt];
            stdin = null;
        }
        else if (OperatingSystem.IsWindows() && IsWindowsBatchWrapper(cli))
        {
            fileName = cli;
            args = flags;
            stdin = prompt;
        }
        else
        {
            fileName = cli;
            args = [.. flags, prompt];
            stdin = null;
        }
        var (_, stdout, stderr) = await CliUtil.RunCaptureAsync(fileName, args, root, 90_000, stdin: stdin).ConfigureAwait(false);
        var message = PickAgentMessage(stdout, stderr);
        if (!string.IsNullOrWhiteSpace(message))
            return message;
        var err = string.Join('\n', new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Cursor Agent 沒有回傳說明。" : err);
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
