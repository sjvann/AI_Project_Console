namespace AiProject.Company.Domain;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}

public sealed class StaffAccount
{
    public Guid Id { get; private set; }
    public Guid PersonId { get; private set; }
    public string UserName { get; private set; } = "";
    public string PasswordHash { get; private set; } = "";
    public PlatformRole Role { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public static Outcome<StaffAccount> Create(string userName, Guid personId, PlatformRole role, string passwordHash, DateTimeOffset now)
    {
        if (role.UsesGitHubSignIn())
            return Outcome<StaffAccount>.Fail(ErrorCodes.InvalidState, Messages.GitHubInviteInstead);
        if (role.IsVendor() && role != PlatformRole.VendorAdmin)
            return Outcome<StaffAccount>.Fail(ErrorCodes.InvalidState, Messages.GitHubInviteInstead);
        var normalized = NormalizeUserName(userName);
        if (normalized.Length < 2)
            return Outcome<StaffAccount>.Fail(ErrorCodes.Required, Messages.Required("登入帳號"));
        if (!normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            return Outcome<StaffAccount>.Fail(ErrorCodes.InvalidState, "登入帳號只能用英數、點、底線或連字號。");
        if (string.IsNullOrWhiteSpace(passwordHash))
            return Outcome<StaffAccount>.Fail(ErrorCodes.Required, Messages.Required("密碼"));
        return Outcome<StaffAccount>.Success(new StaffAccount
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            UserName = normalized,
            PasswordHash = passwordHash,
            Role = role,
            Enabled = true,
            CreatedAt = now,
            PasswordChangedAt = now,
        });
    }

    public static Outcome EnsurePassword(string password) =>
        string.IsNullOrEmpty(password) || password.Length < 8
            ? Outcome.Fail(ErrorCodes.InvalidState, Messages.WeakPassword)
            : Outcome.Success();

    public static string NormalizeUserName(string userName) => (userName ?? "").Trim().ToLowerInvariant();

    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException(ErrorCodes.Required, Messages.Required("密碼"));
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
    }

    public Outcome Disable(int remainingEnabledOwners)
    {
        if (Role == PlatformRole.Owner && remainingEnabledOwners <= 1)
            return Outcome.Fail(ErrorCodes.LastOwner, Messages.LastOwner);
        Enabled = false;
        return Outcome.Success();
    }

    public void Enable() => Enabled = true;
}
