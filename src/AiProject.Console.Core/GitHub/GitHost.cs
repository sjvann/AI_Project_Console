namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 認證對象：種類 + 主機。不要把 github.com 寫死在登入或網址組裝裡。
/// </summary>
public static class GitHost
{
    public const string PublicHostname = "github.com";
    public const string KindGithub = "github";
    public const string KindEnterprise = "github-enterprise";
    public const string KindGitlab = "gitlab";
    public const string KindGitea = "gitea";
    public const string KindAzure = "azure-devops";
    public const string KindGitOnly = "git-only";

    public static IReadOnlyList<(string Id, string Label, bool Ready)> Kinds { get; } =
    [
        (KindGithub, "GitHub.com", true),
        (KindEnterprise, "GitHub Enterprise", true),
        (KindGitlab, "GitLab", false),
        (KindGitea, "Gitea", false),
        (KindAzure, "Azure DevOps", false),
        (KindGitOnly, "僅 Git（無任務主機）", true),
    ];

    public static string Normalize(string? host)
    {
        var raw = (host ?? "").Trim();
        if (string.IsNullOrEmpty(raw))
            return PublicHostname;
        raw = raw.Replace('\\', '/');
        foreach (var prefix in new[] { "https://", "http://", "ssh://", "git@" })
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[prefix.Length..];
                break;
            }
        }
        raw = raw.Split('/')[0];
        var colon = raw.LastIndexOf(':');
        if (colon > 0 && raw.IndexOf('.') >= 0 && colon > raw.LastIndexOf('.'))
            raw = raw[..colon];
        return string.IsNullOrWhiteSpace(raw) ? PublicHostname : raw.Trim().ToLowerInvariant();
    }

    public static bool IsPublic(string? host) =>
        string.Equals(Normalize(host), PublicHostname, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeKind(string? kind)
    {
        var raw = (kind ?? "").Trim().ToLowerInvariant();
        return raw switch
        {
            KindEnterprise or "ghe" or "enterprise" => KindEnterprise,
            KindGitlab or "gl" => KindGitlab,
            KindGitea or "gogs" => KindGitea,
            KindAzure or "ado" or "azure" => KindAzure,
            KindGitOnly or "git" or "none" => KindGitOnly,
            _ => KindGithub,
        };
    }

    public static bool IssuesReady(string? kind)
    {
        var k = NormalizeKind(kind);
        return k is KindGithub or KindEnterprise;
    }

    public static string KindLabel(string? kind)
    {
        var id = NormalizeKind(kind);
        var found = Kinds.FirstOrDefault(k => k.Id == id);
        return string.IsNullOrEmpty(found.Label) ? id : found.Label;
    }

    public static string HostFromUrl(string? url)
    {
        var text = (url ?? "").Trim();
        if (string.IsNullOrEmpty(text))
            return "";
        return Normalize(text);
    }

    public static string InferKind(string? host, string? url = null)
    {
        var h = string.IsNullOrWhiteSpace(url) ? Normalize(host) : HostFromUrl(url);
        if (string.IsNullOrEmpty(h) || IsPublic(h))
            return KindGithub;
        if (h.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
            return KindGitlab;
        if (h.Contains("gitea", StringComparison.OrdinalIgnoreCase) || h.Contains("gogs", StringComparison.OrdinalIgnoreCase))
            return KindGitea;
        if (h.Contains("dev.azure", StringComparison.OrdinalIgnoreCase)
            || h.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return KindAzure;
        return KindEnterprise;
    }

    public static string WebBase(string? host) => "https://" + Normalize(host);

    public static string WebUrl(string? host, string owner, string repo)
    {
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            return "";
        return $"{WebBase(host)}/{owner.Trim()}/{repo.Trim()}";
    }

    public static string GitUrl(string? host, string owner, string repo)
    {
        var web = WebUrl(host, owner, repo);
        return string.IsNullOrEmpty(web) ? "" : web + ".git";
    }

    public static string DisplayAccount(string login, string? host) =>
        string.IsNullOrWhiteSpace(login)
            ? ""
            : IsPublic(host) ? "@" + login.Trim() : "@" + login.Trim() + " · " + Normalize(host);
}
