using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record GithubAccount(string Login, bool GhInstalled, string Host = GitHost.PublicHostname)
{
    public static GithubAccount None { get; } = new("", false);

    public bool LoggedIn => !string.IsNullOrEmpty(Login);

    public string Display() => GitHost.DisplayAccount(Login, Host);
}

public static class GithubAccountStatus
{
    public const string Ok = "ok";
    public const string Wait = "wait";
    public const string Danger = "danger";

    public static string Tone(bool loggedIn, bool needsAttention) =>
        needsAttention ? Wait : loggedIn ? Ok : Danger;

    public static string Title(bool loggedIn, GitBriefStatus? brief)
    {
        if (brief is { DirtyCount: > 0 } dirty)
            return $"有 {dirty.DirtyCount} 筆未提交變更，請到操作台提交";
        var reason = brief?.LeaveBlockReason();
        if (!string.IsNullOrEmpty(reason))
            return reason;
        if (brief is { Behind: > 0 } behind)
            return $"目前分支落後遠端 {behind.Behind} 個提交，請先同步";
        return loggedIn ? "已登入 GitHub" : "尚未登入 GitHub";
    }
}

public sealed record GithubLoginPrompt(string DeviceCode, string BrowserUrl, bool BrowserOpened);

public static class GitHubAuth
{
    public const int LoginTimeoutMs = 600_000;

    public static string? ParseUserLogin(string text)
    {
        var raw = (text ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return null;
        var bare = raw.Trim('"');
        if (LooksLikeLogin(bare))
            return bare;
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonValue value)
            {
                var login = value.ToString().Trim().Trim('"');
                return LooksLikeLogin(login) ? login : null;
            }
            var fromObj = JsonUtil.Str(JsonUtil.Obj(node)?["login"]);
            return LooksLikeLogin(fromObj) ? fromObj : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static bool LooksLikeLogin(string value) =>
        value.Length is > 0 and <= 39
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && !value.Contains('/', StringComparison.Ordinal);

    public static async Task<GithubAccount> CurrentAsync(
        string? cwd = null,
        string? host = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return GithubAccount.None;
        var hostname = GitHost.Normalize(host);
        var cfg = new GithubConfig { Host = hostname };
        var (code, stdout, _) = await GhCli.RunCaptureAsync(
            ["api", "user", "--jq", ".login"], cwd, cfg, 30_000, ct).ConfigureAwait(false);
        if (code != 0)
            return new GithubAccount("", true, hostname);
        var login = ParseUserLogin(stdout);
        return login is null ? new GithubAccount("", true, hostname) : new GithubAccount(login, true, hostname);
    }

    static readonly Regex AnsiEscape = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    static readonly Regex DeviceCodePattern = new(
        @"one-time code(?:\s*\(|:\s*)([A-Z0-9]{4}-[A-Z0-9]{4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex HttpUrlPattern = new(@"https://[^\s<>""']+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string StripAnsi(string? text) =>
        string.IsNullOrEmpty(text) ? "" : AnsiEscape.Replace(text, "");

    public static string? ParseDeviceCode(string? text)
    {
        var m = DeviceCodePattern.Match(StripAnsi(text));
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static string? ParseBrowserUrl(string? text)
    {
        var clean = StripAnsi(text);
        var m = HttpUrlPattern.Match(clean);
        if (!m.Success)
            return null;
        var url = m.Value.TrimEnd('.', ',', ')', ']', '"', '\'');
        if (url.Contains("login/device", StringComparison.OrdinalIgnoreCase)
            || url.Contains("login/oauth", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("web browser", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("Open this URL", StringComparison.OrdinalIgnoreCase)
            || clean.Contains("in your browser", StringComparison.OrdinalIgnoreCase))
            return url;
        return null;
    }

    public static async Task<(bool Ok, string Message)> LoginWebAsync(
        string? cwd = null,
        string? host = null,
        IProgress<GithubLoginPrompt>? progress = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return (false, "尚未安裝 GitHub CLI（gh）。請先安裝 https://cli.github.com/ 再登入。");
        var hostname = GitHost.Normalize(host);
        var deviceCode = "";
        var browserUrl = "";
        var browserOpened = false;
        var gate = new object();

        void OnLine(string line)
        {
            string? toOpen = null;
            GithubLoginPrompt? prompt;
            lock (gate)
            {
                var parsedCode = ParseDeviceCode(line);
                var parsedUrl = ParseBrowserUrl(line);
                if (parsedCode is null && parsedUrl is null)
                    return;
                if (parsedCode is not null)
                    deviceCode = parsedCode;
                if (parsedUrl is not null && browserUrl.Length == 0)
                    browserUrl = parsedUrl;
                if (!browserOpened && browserUrl.Length > 0)
                {
                    toOpen = browserUrl;
                    browserOpened = true;
                }
                prompt = new GithubLoginPrompt(deviceCode, browserUrl, toOpen is not null || browserOpened);
            }
            progress?.Report(prompt);
            if (toOpen is null)
                return;
            var opened = TryOpenBrowser(toOpen);
            lock (gate)
            {
                browserOpened = opened;
                prompt = new GithubLoginPrompt(deviceCode, browserUrl, opened);
            }
            progress?.Report(prompt);
        }

        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["auth", "login", "--hostname", hostname, "--git-protocol", "https", "--web"],
            cwd,
            LoginTimeoutMs,
            ct,
            onLine: OnLine).ConfigureAwait(false);

        string fallbackUrl;
        string fallbackCode;
        bool alreadyOpened;
        lock (gate)
        {
            deviceCode = ParseDeviceCode(output) ?? deviceCode;
            browserUrl = string.IsNullOrEmpty(browserUrl) ? (ParseBrowserUrl(output) ?? "") : browserUrl;
            fallbackUrl = browserUrl;
            fallbackCode = deviceCode;
            alreadyOpened = browserOpened;
        }
        if (!alreadyOpened && fallbackUrl.Length > 0)
        {
            var opened = TryOpenBrowser(fallbackUrl);
            lock (gate)
                browserOpened = opened || browserOpened;
            progress?.Report(new GithubLoginPrompt(fallbackCode, fallbackUrl, opened || alreadyOpened));
        }

        if (code == 0)
            return (true, string.IsNullOrEmpty(output) ? "已登入 " + hostname : output);
        return (false, string.IsNullOrEmpty(output) ? "登入未完成或已取消。" : output);
    }

    static bool TryOpenBrowser(string url)
    {
        try
        {
            CliUtil.OpenUrl(url);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Ok, string Message)> LogoutAsync(
        string? cwd = null,
        string? host = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return (false, "尚未安裝 GitHub CLI（gh）。");
        var hostname = GitHost.Normalize(host);
        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["auth", "logout", "--hostname", hostname],
            cwd,
            60_000,
            ct,
            stdin: "Y\n").ConfigureAwait(false);
        if (code == 0)
            return (true, string.IsNullOrEmpty(output) ? "已登出 " + hostname : output);
        return (false, string.IsNullOrEmpty(output) ? "登出失敗。" : output);
    }
}
