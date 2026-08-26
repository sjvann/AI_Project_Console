using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record GithubAccount(string Login, bool GhInstalled)
{
    public static GithubAccount None { get; } = new("", false);

    public bool LoggedIn => !string.IsNullOrEmpty(Login);

    public string Display() => LoggedIn ? "@" + Login : "";
}

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

    public static async Task<GithubAccount> CurrentAsync(string? cwd = null, CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return GithubAccount.None;
        var (code, stdout, _) = await CliUtil.RunCaptureAsync(
            "gh", ["api", "user", "--jq", ".login"], cwd, 30_000, ct).ConfigureAwait(false);
        if (code != 0)
            return new GithubAccount("", true);
        var login = ParseUserLogin(stdout);
        return login is null ? new GithubAccount("", true) : new GithubAccount(login, true);
    }

    public static async Task<(bool Ok, string Message)> LoginWebAsync(
        string? cwd = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return (false, "尚未安裝 GitHub CLI（gh）。請先安裝 https://cli.github.com/ 再登入。");
        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["auth", "login", "--hostname", "github.com", "--git-protocol", "https", "--web"],
            cwd,
            LoginTimeoutMs,
            ct,
            stdin: "\n").ConfigureAwait(false);
        if (code == 0)
            return (true, string.IsNullOrEmpty(output) ? "已登入 GitHub" : output);
        return (false, string.IsNullOrEmpty(output) ? "登入未完成或已取消。" : output);
    }

    public static async Task<(bool Ok, string Message)> LogoutAsync(
        string? cwd = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return (false, "尚未安裝 GitHub CLI（gh）。");
        var (code, output) = await CliUtil.RunAsync(
            "gh",
            ["auth", "logout", "--hostname", "github.com"],
            cwd,
            60_000,
            ct,
            stdin: "Y\n").ConfigureAwait(false);
        if (code == 0)
            return (true, string.IsNullOrEmpty(output) ? "已登出 GitHub" : output);
        return (false, string.IsNullOrEmpty(output) ? "登出失敗。" : output);
    }
}
