using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class PeopleCommands
{
    readonly IPersonRepository _people;
    readonly IVendorRepository _vendors;
    readonly IUnmatchedUploadRepository _unmatched;
    readonly IInvitationRepository _invites;
    readonly IAssignmentRepository _assignments;
    readonly ITimesheetRepository _timesheets;
    readonly IAuthorizationGate _auth;
    readonly IFieldEncryptor _encryptor;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public PeopleCommands(
        IPersonRepository people,
        IVendorRepository vendors,
        IUnmatchedUploadRepository unmatched,
        IInvitationRepository invites,
        IAssignmentRepository assignments,
        ITimesheetRepository timesheets,
        IAuthorizationGate auth,
        IFieldEncryptor encryptor,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock,
        ICurrentUser user)
    {
        _people = people;
        _vendors = vendors;
        _unmatched = unmatched;
        _invites = invites;
        _assignments = assignments;
        _timesheets = timesheets;
        _auth = auth;
        _encryptor = encryptor;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome<Guid>> CreatePersonAsync(string displayName, EmploymentKind kind, Guid? vendorId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManagePeople, vendorId: vendorId);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        if (kind == EmploymentKind.VendorStaff && vendorId is Guid vid)
        {
            if (_user.Role == PlatformRole.VendorAdmin && _user.VendorId != vid)
                return Outcome<Guid>.Fail(ErrorCodes.Forbidden, Messages.Forbidden);
        }
        var person = Person.Create(displayName, kind, vendorId, _clock.UtcNow);
        await _people.AddAsync(person, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(person.Id);
    }

    public async Task<Outcome> UpdatePersonAsync(
        Guid id,
        string displayName,
        string? email,
        string? department,
        Guid? managerId,
        decimal weeklyCap,
        IEnumerable<string> skills,
        PersonStatus status,
        bool costExempt,
        IEnumerable<UnavailableRange> unavailable,
        decimal? monthlySalary,
        decimal? hourlyRate,
        string? rateReason,
        CancellationToken ct = default)
    {
        var person = await _people.GetAsync(id, ct);
        if (person is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("人員"));
        var gate = _auth.Ensure(PlatformCapability.ManagePeople, vendorId: person.VendorId);
        if (!gate.Ok)
            return gate;
        if (_user.Role == PlatformRole.VendorAdmin)
        {
            person.UpdateProfile(displayName, email, department, managerId, weeklyCap, skills);
            person.ReplaceUnavailable(unavailable);
            await _uow.SaveChangesAsync(ct);
            return Outcome.Success();
        }
        person.UpdateProfile(displayName, email, department, managerId, weeklyCap, skills);
        person.SetStatus(status);
        person.SetCostAllocationExempt(costExempt);
        person.ReplaceUnavailable(unavailable);
        if (monthlySalary is not null || hourlyRate is not null)
        {
            var rateGate = _auth.Ensure(PlatformCapability.ChangeRate, personId: id);
            if (!rateGate.Ok)
                return rateGate;
            if (string.IsNullOrWhiteSpace(rateReason))
                return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
            var salaryCipher = monthlySalary is decimal s ? _encryptor.EncryptDecimal(s) : person.MonthlySalaryCipher;
            var hourlyCipher = hourlyRate is decimal h ? _encryptor.EncryptDecimal(h) : person.InternalHourlyRateCipher;
            person.SetCompensationCiphers(salaryCipher, hourlyCipher);
            await _audit.SensitiveChange(AuditActions.ChangeRate, "Person", id, rateReason, null, new { monthlySalary, hourlyRate }, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> BindGitHubAsync(Guid personId, string login, CancellationToken ct = default)
    {
        var person = await _people.GetAsync(personId, ct);
        if (person is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("人員"));
        var gate = _auth.Ensure(PlatformCapability.ManagePeople, vendorId: person.VendorId);
        if (!gate.Ok)
            return gate;
        var result = person.BindGitHub(login, other =>
        {
            var found = _people.GetByGitHubAsync(other, ct).GetAwaiter().GetResult();
            return found is { Status: PersonStatus.Active, IsDeleted: false } ? found.Id : null;
        });
        if (!result.Ok)
            return result;
        foreach (var item in await _unmatched.ListOpenAsync(ct))
        {
            if (string.Equals(item.GitHubLogin, person.GitHubLogin, StringComparison.OrdinalIgnoreCase))
                item.Bind(person.Id);
        }
        var invite = await _invites.GetByGitHubAsync(person.GitHubLogin!, ct);
        if (invite is not null)
        {
            invite.BindPerson(person.Id);
            if (invite.AcceptedAt is null)
                invite.Accept(_clock.UtcNow);
        }
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.BindGitHub, "Person", personId, "綁定 GitHub", null, login, ct);
        return Outcome.Success();
    }

    public async Task<Outcome> DeactivateAsync(Guid personId, CancellationToken ct = default)
    {
        var person = await _people.GetAsync(personId, ct);
        if (person is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("人員"));
        var gate = _auth.Ensure(PlatformCapability.ManagePeople, vendorId: person.VendorId);
        if (!gate.Ok)
            return gate;
        person.SetStatus(PersonStatus.Inactive);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome<Guid>> CreateVendorAsync(string name, string? taxId, DateOnly from, DateOnly? to, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManagePeople);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var vendor = Vendor.Create(name, taxId, from, to);
        await _vendors.AddAsync(vendor, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(vendor.Id);
    }

    public async Task<Outcome> UpdateVendorAsync(Guid id, string name, string? taxId, Guid? windowPersonId, DateOnly from, DateOnly? to, decimal? defaultRate, string? reason, CancellationToken ct = default)
    {
        var vendor = await _vendors.GetAsync(id, ct);
        if (vendor is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("外包商"));
        var gate = _auth.Ensure(PlatformCapability.ManagePeople, vendorId: id);
        if (!gate.Ok)
            return gate;
        string? cipher = vendor.DefaultRateCipher;
        if (defaultRate is decimal rate)
        {
            var rateGate = _auth.Ensure(PlatformCapability.ChangeRate);
            if (!rateGate.Ok)
                return rateGate;
            if (string.IsNullOrWhiteSpace(reason))
                return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
            cipher = _encryptor.EncryptDecimal(rate);
            await _audit.SensitiveChange(AuditActions.ChangeRate, "Vendor", id, reason, null, rate, ct);
        }
        vendor.Update(name, taxId, windowPersonId, from, to, cipher);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<bool> HasHistoryAsync(Guid personId, CancellationToken ct = default)
    {
        var assignments = await _assignments.ListForPersonAsync(personId, ct);
        var sheets = await _timesheets.ListForPersonAsync(personId, ct);
        return assignments.Count > 0 || sheets.Count > 0;
    }
}
