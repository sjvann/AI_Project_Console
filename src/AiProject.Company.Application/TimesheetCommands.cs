using System.Text.Json;
using AiProject.Company.Contracts;
using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class TimesheetCommands
{
    readonly ITimesheetRepository _timesheets;
    readonly IPersonRepository _people;
    readonly IProjectRepository _projects;
    readonly IUnmatchedUploadRepository _unmatched;
    readonly IInvitationRepository _invites;
    readonly IAuthorizationGate _auth;
    readonly ITimesheetIdempotency _idempotency;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public TimesheetCommands(
        ITimesheetRepository timesheets,
        IPersonRepository people,
        IProjectRepository projects,
        IUnmatchedUploadRepository unmatched,
        IInvitationRepository invites,
        IAuthorizationGate auth,
        ITimesheetIdempotency idempotency,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock,
        ICurrentUser user)
    {
        _timesheets = timesheets;
        _people = people;
        _projects = projects;
        _unmatched = unmatched;
        _invites = invites;
        _auth = auth;
        _idempotency = idempotency;
        _uow = uow;
        _audit = audit;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome<Guid>> UploadAsync(TimesheetUploadRequest request, string gitHubLogin, Guid? actingPersonId, Guid? actingVendorId, bool vendorSubmit, CancellationToken ct = default)
    {
        if (request.HasForbiddenFields())
            return Outcome<Guid>.Fail(ErrorCodes.InvalidState, "上傳內容只能包含時段、專案識別、狀態圖與 Issue 編號。");
        var person = actingPersonId is Guid pid
            ? await _people.GetAsync(pid, ct)
            : await _people.GetByGitHubAsync(gitHubLogin, ct);
        var invite = await _invites.GetByGitHubAsync(gitHubLogin, ct);
        if (person is null && invite is not null)
            person = await ProvisionFromInviteAsync(invite, gitHubLogin, ct);
        if (person is null)
        {
            await _unmatched.AddAsync(UnmatchedUpload.Capture(gitHubLogin, request.LocalSlotId, JsonSerializer.Serialize(request), _clock.UtcNow), ct);
            await _uow.SaveChangesAsync(ct);
            return Outcome<Guid>.Fail(ErrorCodes.UnmatchedPerson, Messages.UnmatchedPerson);
        }
        if (invite is not null && invite.AcceptedAt is null)
            invite.Accept(_clock.UtcNow);
        var gate = vendorSubmit
            ? _auth.Ensure(PlatformCapability.SubmitVendorTimesheet, vendorId: person.VendorId)
            : _auth.Ensure(PlatformCapability.UploadTimesheet, personId: person.Id);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        if (vendorSubmit && actingVendorId is Guid vendor && person.VendorId != vendor)
            return Outcome<Guid>.Fail(ErrorCodes.Forbidden, Messages.Forbidden);
        var project = await _projects.GetAsync(request.ProjectId, ct);
        if (project is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var existing = await _timesheets.GetByLocalSlotAsync(request.LocalSlotId, ct);
        var decision = _idempotency.Decide(existing, request.IsCorrection);
        if (!decision.Ok)
            return Outcome<Guid>.Fail(decision.Code, decision.Message);
        var chart = request.Chart.Select(c => new StatusChartCell(c.ProjectId, c.Date, c.Intensity, c.Note));
        if (existing is null || (existing.Status == TimesheetStatus.Approved && request.IsCorrection))
        {
            var sheet = Timesheet.Upload(request.LocalSlotId, person.Id, request.ProjectId, request.WorkDate, request.Hours, request.IssueNumbers, chart, request.IsCorrection, request.CorrectsLocalSlotId, _clock.UtcNow);
            await _timesheets.AddAsync(sheet, ct);
            await _uow.SaveChangesAsync(ct);
            if (request.IsCorrection)
                await _audit.SensitiveChange(AuditActions.CorrectTimesheet, "Timesheet", sheet.Id, "更正時段", existing?.Id, sheet.Id, ct);
            return Outcome<Guid>.Success(sheet.Id);
        }
        existing.ReplacePending(request.ProjectId, request.WorkDate, request.Hours, request.IssueNumbers, chart, _clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(existing.Id);
    }

    public async Task<Outcome> ConfirmAsync(Guid timesheetId, CancellationToken ct = default)
    {
        var sheet = await _timesheets.GetAsync(timesheetId, ct);
        if (sheet is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("時段"));
        var gate = _auth.Ensure(PlatformCapability.ConfirmTimesheet, projectId: sheet.ProjectId);
        if (!gate.Ok)
            return gate;
        sheet.Confirm(_clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> ReturnAsync(Guid timesheetId, string reason, CancellationToken ct = default)
    {
        var sheet = await _timesheets.GetAsync(timesheetId, ct);
        if (sheet is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("時段"));
        var gate = _auth.Ensure(PlatformCapability.ConfirmTimesheet, projectId: sheet.ProjectId);
        if (!gate.Ok)
            return gate;
        sheet.ReturnToEngineer(reason);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    async Task<Person> ProvisionFromInviteAsync(Invitation invite, string gitHubLogin, CancellationToken ct)
    {
        var kind = invite.Role.IsVendor() ? EmploymentKind.VendorStaff : EmploymentKind.Freelance;
        var person = Person.Create(gitHubLogin, kind, invite.VendorId, _clock.UtcNow);
        person.BindGitHub(gitHubLogin, _ => null).ThrowIfFailed();
        invite.BindPerson(person.Id);
        await _people.AddAsync(person, ct);
        return person;
    }
}
