namespace AiProject.Company.Domain;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today(string timeZoneId);
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? PersonId { get; }
    string UserName { get; }
    string GitHubLogin { get; }
    string DisplayName { get; }
    PlatformRole Role { get; }
    Guid? VendorId { get; }
    IReadOnlySet<Guid> AuthorizedProjectIds { get; }
}

public interface IAuthorizationGate
{
    Outcome Ensure(PlatformCapability capability, Guid? projectId = null, Guid? personId = null, Guid? vendorId = null);
    bool Can(PlatformCapability capability, Guid? projectId = null, Guid? personId = null, Guid? vendorId = null);
}

public sealed record AuditEntry(
    Guid Id,
    DateTimeOffset At,
    Guid? ActorPersonId,
    string ActorLogin,
    string Action,
    string EntityType,
    Guid? EntityId,
    string Reason,
    string BeforeJson,
    string AfterJson,
    bool IsSensitiveRead);

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> QueryAsync(string? entityType, Guid? entityId, CancellationToken cancellationToken = default);
}

public interface IFieldEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string EncryptDecimal(decimal value) => Encrypt(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    decimal DecryptDecimal(string ciphertext) =>
        decimal.Parse(Decrypt(ciphertext), System.Globalization.CultureInfo.InvariantCulture);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IGitHubAuthenticator
{
    string AuthorizationEndpoint { get; }
}

public sealed record GitHubIssueRef(string Repo, int Number, string Title, string? AssigneeLogin, DateTimeOffset UpdatedAt, string Url);

public sealed record GitHubMember(string Login, string DisplayName);

public interface IGitHubDirectory
{
    Task<IReadOnlyList<GitHubMember>> ListOrgMembersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListReposAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GitHubIssueRef>> ListOpenIssuesAsync(string ownerRepo, CancellationToken cancellationToken = default);
    Task<Outcome> AddAssigneeAsync(string ownerRepo, int issueNumber, string login, CancellationToken cancellationToken = default);
    Task<string?> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken = default);
}

public interface IMarginThresholds
{
    decimal YellowPercent { get; }
    decimal RedPercent { get; }
    string Classify(decimal? marginPercent);
}

public interface ITimesheetIdempotency
{
    Outcome Decide(Timesheet? existing, bool incomingIsCorrection);
}

public interface IPayrollCalculator
{
    string Kind { get; }
    bool AppliesTo(Person person, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses);
    PayrollLine Calculate(Person person, CompanySettings settings, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses, DateOnly periodStart, DateOnly periodEnd);
}

public interface IRevenueRecognizer
{
    string Kind { get; }
    decimal Recognize(Project project, DateOnly asOf, IReadOnlyList<Timesheet> approved, decimal billableHours);
}

public interface IAvailabilityCalculator
{
    PersonAvailability ForWeek(Person person, DateOnly weekStart, IReadOnlyList<Assignment> assignments);
}

public interface IContractStaffingPolicy
{
    Outcome CanAssign(Person person, Project project, Contract contract);
}

public interface IAssignmentSuggester
{
    IReadOnlyList<AssignmentSuggestion> Suggest(Project project, IReadOnlyList<Person> candidates, IReadOnlyList<Assignment> weekAssignments, DateOnly weekStart);
}

public interface IProjectHealthPolicy
{
    ProjectHealth Evaluate(Project project, DateOnly today, int unassignedIssueDays, bool hoursMissing, decimal? marginPercent, bool requirementsCatalogReady = true);
}

public interface IHomeRouteResolver
{
    string PathFor(PlatformRole role);
}

public sealed record PersonAvailability(Guid PersonId, decimal CapHours, decimal AssignedHours, decimal RemainingHours, bool Overload)
{
    public decimal RemainingHoursClamped => RemainingHours < 0 ? 0 : RemainingHours;
}

public sealed record AssignmentSuggestion(Guid PersonId, string DisplayName, decimal RemainingHours, int SkillHits, string Reason);

public enum HealthTone
{
    Green,
    Yellow,
    Red,
}

public sealed record ProjectHealth(HealthTone Schedule, HealthTone Staffing, HealthTone Margin, string ScheduleLabel, string StaffingLabel, string MarginLabel)
{
    public HealthTone Worst =>
        Schedule == HealthTone.Red || Staffing == HealthTone.Red || Margin == HealthTone.Red
            ? HealthTone.Red
            : Schedule == HealthTone.Yellow || Staffing == HealthTone.Yellow || Margin == HealthTone.Yellow
                ? HealthTone.Yellow
                : HealthTone.Green;
}
