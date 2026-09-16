using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class ReportingQueries
{
    readonly ITimesheetRepository _timesheets;
    readonly IUnmatchedUploadRepository _unmatched;
    readonly IReportingApiKeyRepository _keys;
    readonly IAuthorizationGate _auth;

    public ReportingQueries(
        ITimesheetRepository timesheets,
        IUnmatchedUploadRepository unmatched,
        IReportingApiKeyRepository keys,
        IAuthorizationGate auth)
    {
        _timesheets = timesheets;
        _unmatched = unmatched;
        _keys = keys;
        _auth = auth;
    }

    public bool CanViewInbox =>
        _auth.Can(PlatformCapability.ConfirmTimesheet)
        || _auth.Can(PlatformCapability.ManageSettings)
        || _auth.Can(PlatformCapability.ViewWarRoom)
        || _auth.Can(PlatformCapability.ManagePeople);

    public async Task<ReportingBadge> BadgeAsync(CancellationToken ct = default)
    {
        if (!CanViewInbox)
            return ReportingBadge.Empty;
        var pending = (await _timesheets.ListAsync(ct)).Count(t => t.Status == TimesheetStatus.PendingPm);
        var unmatched = (await _unmatched.ListOpenAsync(ct)).Count;
        return new ReportingBadge(pending, unmatched);
    }

    public async Task<IReadOnlyList<ReportingApiKey>> KeysAsync(CancellationToken ct = default)
    {
        if (!_auth.Can(PlatformCapability.ManageSettings))
            return [];
        return await _keys.ListAsync(ct);
    }
}

public sealed record ReportingBadge(int Pending, int Unmatched)
{
    public static ReportingBadge Empty { get; } = new(0, 0);
    public int Total => Pending + Unmatched;
}
