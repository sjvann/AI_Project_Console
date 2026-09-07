using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class ProjectHealthPolicyAdapter : IProjectHealthPolicy
{
    readonly ISettingsRepository _settings;

    public ProjectHealthPolicyAdapter(ISettingsRepository settings) => _settings = settings;

    public ProjectHealth Evaluate(Project project, DateOnly today, int unassignedIssueDays, bool hoursMissing, decimal? marginPercent, bool requirementsCatalogReady = true)
    {
        var company = _settings.GetAsync().GetAwaiter().GetResult();
        return new ProjectHealthPolicy(company).Evaluate(project, today, unassignedIssueDays, hoursMissing, marginPercent, requirementsCatalogReady);
    }
}

public static class AuditActions
{
    public const string ChangeRate = "change_rate";
    public const string LockPayroll = "lock_payroll";
    public const string ForceOverload = "force_overload";
    public const string ViewPayslip = "view_payslip";
    public const string CorrectTimesheet = "correct_timesheet";
    public const string UpdateSettings = "update_settings";
    public const string Invite = "invite";
    public const string BindGitHub = "bind_github";
    public const string EstablishProject = "establish_project";
}

public sealed class AuditWriter
{
    readonly IAuditLog _log;
    readonly ICurrentUser _user;
    readonly IClock _clock;

    public AuditWriter(IAuditLog log, ICurrentUser user, IClock clock)
    {
        _log = log;
        _user = user;
        _clock = clock;
    }

    public Task SensitiveChange(string action, string entityType, Guid? entityId, string reason, object? before, object? after, CancellationToken ct = default) =>
        _log.WriteAsync(new AuditEntry(Guid.NewGuid(), _clock.UtcNow, _user.PersonId, Actor(), action, entityType, entityId, reason, Json(before), Json(after), false), ct);

    public Task SensitiveRead(string action, string entityType, Guid? entityId, CancellationToken ct = default) =>
        _log.WriteAsync(new AuditEntry(Guid.NewGuid(), _clock.UtcNow, _user.PersonId, Actor(), action, entityType, entityId, "", "", "", true), ct);

    string Actor() => string.IsNullOrEmpty(_user.GitHubLogin) ? _user.UserName : _user.GitHubLogin;

    static string Json(object? value) => value is null ? "" : System.Text.Json.JsonSerializer.Serialize(value);
}
