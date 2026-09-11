using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class AccountCommands
{
    readonly IStaffAccountRepository _accounts;
    readonly IPersonRepository _people;
    readonly IAuthorizationGate _auth;
    readonly IPasswordHasher _hasher;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public AccountCommands(
        IStaffAccountRepository accounts,
        IPersonRepository people,
        IAuthorizationGate auth,
        IPasswordHasher hasher,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock,
        ICurrentUser user)
    {
        _accounts = accounts;
        _people = people;
        _auth = auth;
        _hasher = hasher;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome<Guid>> CreateAsync(string userName, string displayName, PlatformRole role, string password, Guid? vendorId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.InviteUser, vendorId: vendorId);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        if (role == PlatformRole.Owner && _user.Role != PlatformRole.Owner)
            return Outcome<Guid>.Fail(ErrorCodes.Forbidden, Messages.Forbidden);
        if (role.UsesGitHubSignIn())
            return Outcome<Guid>.Fail(ErrorCodes.InvalidState, Messages.GitHubInviteInstead);
        var passwordCheck = StaffAccount.EnsurePassword(password);
        if (!passwordCheck.Ok)
            return Outcome<Guid>.Fail(passwordCheck.Code, passwordCheck.Message);
        var key = StaffAccount.NormalizeUserName(userName);
        if (await _accounts.GetByUserNameAsync(key, ct) is not null)
            return Outcome<Guid>.Fail(ErrorCodes.UsernameTaken, Messages.UsernameTaken);
        var kind = role == PlatformRole.VendorAdmin ? EmploymentKind.VendorStaff : EmploymentKind.FullTime;
        var person = Person.Create(displayName, kind, role == PlatformRole.VendorAdmin ? vendorId : null, _clock.UtcNow);
        await _people.AddAsync(person, ct);
        var created = StaffAccount.Create(key, person.Id, role, _hasher.Hash(password), _clock.UtcNow);
        if (!created.Ok)
            return Outcome<Guid>.Fail(created.Code, created.Message);
        await _accounts.AddAsync(created.Value!, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.Invite, "StaffAccount", created.Value!.Id, "開公司帳戶", null, key, ct);
        return Outcome<Guid>.Success(created.Value.Id);
    }

    public async Task<Outcome> ResetPasswordAsync(Guid accountId, string password, string reason, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.InviteUser);
        if (!gate.Ok)
            return gate;
        var passwordCheck = StaffAccount.EnsurePassword(password);
        if (!passwordCheck.Ok)
            return passwordCheck;
        if (string.IsNullOrWhiteSpace(reason))
            return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
        var account = await _accounts.GetAsync(accountId, ct);
        if (account is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("帳戶"));
        account.SetPasswordHash(_hasher.Hash(password), _clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.Invite, "StaffAccount", account.Id, reason, account.UserName, "reset_password", ct);
        return Outcome.Success();
    }

    public async Task<Outcome> ChangeOwnPasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated)
            return Outcome.Fail(ErrorCodes.Unauthenticated, Messages.Unauthenticated);
        var passwordCheck = StaffAccount.EnsurePassword(newPassword);
        if (!passwordCheck.Ok)
            return passwordCheck;
        var account = await _accounts.GetByUserNameAsync(_user.UserName, ct);
        if (account is null)
            return Outcome.Fail(ErrorCodes.InvalidState, "這個登入沒有公司帳戶密碼。後台請用公司帳戶。");
        if (!_hasher.Verify(account.PasswordHash, currentPassword))
            return Outcome.Fail(ErrorCodes.BadCredentials, Messages.BadCredentials);
        account.SetPasswordHash(_hasher.Hash(newPassword), _clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetEnabledAsync(Guid accountId, bool enabled, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.InviteUser);
        if (!gate.Ok)
            return gate;
        var account = await _accounts.GetAsync(accountId, ct);
        if (account is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("帳戶"));
        if (!enabled)
        {
            var owners = await _accounts.CountEnabledOwnersAsync(ct);
            var result = account.Disable(owners);
            if (!result.Ok)
                return result;
        }
        else
            account.Enable();
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<StaffAccount?> AuthenticateAsync(string userName, string password, CancellationToken ct = default)
    {
        var account = await _accounts.GetByUserNameAsync(StaffAccount.NormalizeUserName(userName), ct);
        if (account is null || !account.Enabled)
            return null;
        return _hasher.Verify(account.PasswordHash, password) ? account : null;
    }
}
