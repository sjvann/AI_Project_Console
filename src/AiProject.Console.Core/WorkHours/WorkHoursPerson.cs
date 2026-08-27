using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.WorkHours;

public static class WorkHoursPerson
{
    public static string LocalKey(string? userName = null)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;
        return "local:" + Normalize(user);
    }

    public static string GithubKey(string login, string? host = null)
    {
        var user = Normalize(login);
        if (string.IsNullOrEmpty(user))
            return LocalKey();
        if (string.IsNullOrWhiteSpace(host) || GitHost.IsPublic(host))
            return "github:" + user;
        return "github:" + GitHost.Normalize(host) + ":" + user;
    }

    public static string FromGithubLogin(string? login, string? host = null) =>
        string.IsNullOrWhiteSpace(login) ? LocalKey() : GithubKey(login!, host);

    public static string LabelFromGithub(string? login) =>
        string.IsNullOrWhiteSpace(login) ? LocalLabel() : "@" + login.Trim();

    public static string LocalLabel(string? userName = null)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;
        return string.IsNullOrWhiteSpace(user) ? "本機" : user.Trim() + "（本機）";
    }

    public static bool IsGithub(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.StartsWith("github:", StringComparison.OrdinalIgnoreCase);

    public static bool IsLocal(string? key) =>
        string.IsNullOrWhiteSpace(key)
        || key.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    public static bool BelongsTo(string sessionKey, string personKey)
    {
        if (string.IsNullOrWhiteSpace(personKey))
            return true;
        if (string.Equals(sessionKey, personKey, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(sessionKey))
            return true;
        return IsLocal(sessionKey) && IsGithub(personKey);
    }

    public static bool CanClaim(string sessionKey, string personKey) =>
        IsGithub(personKey) && IsLocal(sessionKey);

    static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();
}
