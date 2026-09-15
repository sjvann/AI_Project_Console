namespace AiProject.Company.Domain;

public enum AssignmentRole
{
    Analyst = 0,
    Engineer = 1,
    Lead = 2,
    Pm = 3,
}

public enum AssignmentSource
{
    Manual = 0,
    FromIssue = 1,
}

public enum AssignmentStatus
{
    Planned = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3,
}

public enum AssignmentSyncState
{
    NotRequired = 0,
    Synced = 1,
    Pending = 2,
}

public sealed class Assignment : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public Guid PersonId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public decimal PlannedHoursPerWeek { get; private set; }
    public AssignmentRole Role { get; private set; }
    public AssignmentSource Source { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public AssignmentSyncState SyncState { get; private set; }
    public List<int> IssueNumbers { get; private set; } = [];
    public Guid? MilestoneId { get; private set; }
    public string? ForceReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Outcome<Assignment> Create(
        Person person,
        Project project,
        Contract contract,
        DateOnly start,
        DateOnly end,
        decimal hoursPerWeek,
        AssignmentRole role,
        AssignmentSource source,
        IContractStaffingPolicy staffing,
        PersonAvailability availability,
        string? forceReason,
        bool canForce,
        bool writeBack,
        DateTimeOffset now)
    {
        var canPerson = person.EnsureCanAssign();
        if (!canPerson.Ok)
            return Outcome<Assignment>.Fail(canPerson.Code, canPerson.Message);
        var canProject = project.EnsureCanAssign();
        if (!canProject.Ok)
            return Outcome<Assignment>.Fail(canProject.Code, canProject.Message);
        var staff = staffing.CanAssign(person, project, contract);
        if (!staff.Ok)
            return Outcome<Assignment>.Fail(staff.Code, staff.Message);
        if (end < start)
            return Outcome<Assignment>.Fail(ErrorCodes.InvalidState, "派工結束日不能早於開始日。");
        if (hoursPerWeek <= 0)
            return Outcome<Assignment>.Fail(ErrorCodes.InvalidState, "規劃小時必須大於 0。");
        var projected = availability.AssignedHours + hoursPerWeek;
        if (projected > availability.CapHours)
        {
            if (!canForce)
                return Outcome<Assignment>.Fail(ErrorCodes.Forbidden, Messages.Forbidden);
            if (string.IsNullOrWhiteSpace(forceReason))
                return Outcome<Assignment>.Fail(ErrorCodes.OverloadRequiresReason, Messages.OverloadRequiresReason);
        }
        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            PersonId = person.Id,
            ProjectId = project.Id,
            Start = start,
            End = end,
            PlannedHoursPerWeek = hoursPerWeek,
            Role = role,
            Source = source,
            Status = AssignmentStatus.Active,
            SyncState = writeBack ? AssignmentSyncState.Pending : AssignmentSyncState.NotRequired,
            ForceReason = string.IsNullOrWhiteSpace(forceReason) ? null : forceReason.Trim(),
            CreatedAt = now,
        };
        return Outcome<Assignment>.Success(assignment);
    }

    public void AttachIssues(IEnumerable<int> numbers) =>
        IssueNumbers = numbers.Where(n => n > 0).Distinct().ToList();

    public void AttachMilestone(Guid? milestoneId) => MilestoneId = milestoneId;

    public void MarkSynced() => SyncState = AssignmentSyncState.Synced;

    public void MarkPendingSync() => SyncState = AssignmentSyncState.Pending;

    public void Cancel()
    {
        if (Status == AssignmentStatus.Cancelled)
            return;
        Status = AssignmentStatus.Cancelled;
    }

    public bool Overlaps(DateOnly rangeStart, DateOnly rangeEnd) =>
        Start <= rangeEnd && End >= rangeStart && Status != AssignmentStatus.Cancelled;
}
