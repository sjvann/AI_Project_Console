using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class SettingsCommands
{
    readonly ISettingsRepository _settings;
    readonly IInvitationRepository _invites;
    readonly IPersonRepository _people;
    readonly IAuthorizationGate _auth;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public SettingsCommands(
        ISettingsRepository settings,
        IInvitationRepository invites,
        IPersonRepository people,
        IAuthorizationGate auth,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock,
        ICurrentUser user)
    {
        _settings = settings;
        _invites = invites;
        _people = people;
        _auth = auth;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome> UpdateAsync(string companyName, string timeZoneId, string currency, decimal yellow, decimal red, bool writeBack, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageSettings);
        if (!gate.Ok)
            return gate;
        var company = await _settings.GetAsync(ct);
        var before = company.CompanyName;
        company.Update(companyName, timeZoneId, currency, yellow, red, writeBack);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.UpdateSettings, "CompanySettings", company.Id, "更新公司設定", before, companyName, ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetThemeAsync(string themeId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageSettings);
        if (!gate.Ok)
            return gate;
        var company = await _settings.GetAsync(ct);
        var before = company.ThemeId;
        company.SetTheme(themeId);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.UpdateSettings, "CompanySettings", company.Id, "更新主題包", before, company.ThemeId, ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetRateAsync(string currency, decimal rate, DateOnly asOf, string reason, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageSettings);
        if (!gate.Ok)
            return gate;
        var company = await _settings.GetAsync(ct);
        try
        {
            company.SetExchangeRate(currency, rate, asOf);
        }
        catch (DomainException ex)
        {
            return Outcome.Fail(ex.Code, ex.Message);
        }

        var code = Currencies.Normalize(currency);
        var audit = string.IsNullOrWhiteSpace(reason)
            ? $"更新 {code} 匯率為 {rate}（對 {company.Currency}）"
            : reason.Trim();
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.ChangeRate, "ExchangeRate", company.Id, audit, code, rate, ct);
        return Outcome.Success();
    }

    public async Task<Outcome> InviteAsync(string gitHubLogin, PlatformRole role, Guid? vendorId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.InviteUser, vendorId: vendorId);
        if (!gate.Ok)
            return gate;
        if (!role.UsesGitHubSignIn())
            return Outcome.Fail(ErrorCodes.InvalidState, Messages.CompanyAccountInstead);
        if (role.IsVendor() && vendorId is null)
            return Outcome.Fail(ErrorCodes.Required, Messages.Required("外包商"));
        var login = gitHubLogin.Trim().ToLowerInvariant();
        var existing = await _invites.GetByGitHubAsync(login, ct);
        if (existing is { Revoked: false })
            return Outcome.Fail(ErrorCodes.Conflict, "這個 GitHub 帳號已經有有效邀請。");
        Invitation invite;
        try
        {
            invite = Invitation.Create(login, role, vendorId, _clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Outcome.Fail(ex.Code, ex.Message);
        }
        var person = await _people.GetByGitHubAsync(login, ct);
        if (person is null)
        {
            var kind = role.IsVendor() ? EmploymentKind.VendorStaff : EmploymentKind.Freelance;
            person = Person.Create(login, kind, vendorId, _clock.UtcNow);
            var bind = person.BindGitHub(login, _ => null);
            if (!bind.Ok)
                return bind;
            await _people.AddAsync(person, ct);
        }
        invite.BindPerson(person.Id);
        await _invites.AddAsync(invite, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.Invite, "Invitation", invite.Id, "招募邀請 GitHub", null, login, ct);
        return Outcome.Success();
    }

    public Task<CompanySettings> GetAsync(CancellationToken ct = default) => _settings.GetAsync(ct);
}
