using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class ApiKeyCommands
{
    readonly IReportingApiKeyRepository _keys;
    readonly IPersonRepository _people;
    readonly IAuthorizationGate _auth;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;

    public ApiKeyCommands(
        IReportingApiKeyRepository keys,
        IPersonRepository people,
        IAuthorizationGate auth,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock)
    {
        _keys = keys;
        _people = people;
        _auth = auth;
        _uow = uow;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>核發回報 API 金鑰；明文只回傳一次。</summary>
    public async Task<Outcome<(ReportingApiKey Key, string Plaintext)>> IssueAsync(Guid personId, string name, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageSettings);
        if (!gate.Ok)
            return Outcome<(ReportingApiKey, string)>.Fail(gate.Code, gate.Message);
        var person = await _people.GetAsync(personId, ct);
        if (person is null)
            return Outcome<(ReportingApiKey, string)>.Fail(ErrorCodes.NotFound, Messages.NotFound("人員"));
        var (key, plaintext) = ReportingApiKey.Issue(personId, name, _clock.UtcNow);
        await _keys.AddAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.IssueApiKey, "ReportingApiKey", key.Id, "核發回報 API 金鑰", null, key.KeyPrefix, ct);
        return Outcome<(ReportingApiKey, string)>.Success((key, plaintext));
    }

    public async Task<Outcome> RevokeAsync(Guid keyId, string reason, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageSettings);
        if (!gate.Ok)
            return gate;
        if (string.IsNullOrWhiteSpace(reason))
            return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
        var key = await _keys.GetAsync(keyId, ct);
        if (key is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("API 金鑰"));
        key.Revoke(_clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.RevokeApiKey, "ReportingApiKey", key.Id, reason.Trim(), key.KeyPrefix, "revoked", ct);
        return Outcome.Success();
    }
}
