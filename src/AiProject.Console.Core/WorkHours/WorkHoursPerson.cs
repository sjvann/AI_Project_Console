namespace AiProject.Console.Core.WorkHours;

public static class WorkHoursPerson
{
    public static string LocalKey(string? userName = null)
    {
        var user = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;
        return "local:" + Normalize(user);
    }

    public static string GithubKey(string login) => "github:" + Normalize(login);

    public static string FromGithubLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? LocalKey() : GithubKey(login!);

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
