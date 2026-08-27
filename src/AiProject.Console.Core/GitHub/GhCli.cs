using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 對已設定主機呼叫 gh。企業主機用 GH_HOST，不要寫死 github.com。
/// </summary>
public static class GhCli
{
    public static IReadOnlyDictionary<string, string>? Env(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || GitHost.IsPublic(host))
            return null;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GH_HOST"] = GitHost.Normalize(host),
        };
    }

    public static IReadOnlyDictionary<string, string>? Env(GithubConfig? cfg) => Env(cfg?.Host);

    public static void AddRepo(List<string> args, GithubConfig? cfg)
    {
        var slug = cfg?.Slug();
        if (string.IsNullOrEmpty(slug))
            return;
        args.Add("--repo");
        args.Add(slug);
    }

    public static Task<(int Code, string Output)> RunAsync(
        IEnumerable<string> args,
        string? cwd = null,
        GithubConfig? cfg = null,
        int timeoutMs = 60_000,
        CancellationToken ct = default,
        string? stdin = null) =>
        CliUtil.RunAsync("gh", args, cwd, timeoutMs, ct, stdin, Env(cfg));

    public static Task<(int Code, string StdOut, string StdErr)> RunCaptureAsync(
        IEnumerable<string> args,
        string? cwd = null,
        GithubConfig? cfg = null,
        int timeoutMs = 60_000,
        CancellationToken ct = default) =>
        CliUtil.RunCaptureAsync("gh", args, cwd, timeoutMs, ct, extraEnv: Env(cfg));
}
