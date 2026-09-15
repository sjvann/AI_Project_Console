namespace AiProject.Company.Domain;

public enum TimesheetStatus
{
    PendingPm = 0,
    Approved = 1,
    Returned = 2,
}

public sealed class Timesheet : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public string LocalSlotId { get; private set; } = "";
    public Guid PersonId { get; private set; }
    public Guid ProjectId { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public decimal Hours { get; private set; }
    public TimesheetStatus Status { get; private set; }
    public string? ReturnReason { get; private set; }
    public List<int> IssueNumbers { get; private set; } = [];
    /// <summary>CHAOSS 貢獻類型，如 code／issue／docs。</summary>
    public List<string> ContributionTypes { get; private set; } = [];
    public List<StatusChartCell> Chart { get; private set; } = [];
    public bool IsCorrection { get; private set; }
    public string? CorrectsLocalSlotId { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public static Timesheet Upload(
        string localSlotId,
        Guid personId,
        Guid projectId,
        DateOnly workDate,
        decimal hours,
        IEnumerable<int> issues,
        IEnumerable<StatusChartCell> chart,
        bool isCorrection,
        string? correctsLocalSlotId,
        DateTimeOffset now,
        IEnumerable<string>? contributionTypes = null)
    {
        if (string.IsNullOrWhiteSpace(localSlotId))
            throw new DomainException(ErrorCodes.Required, Messages.Required("本機時段 ID"));
        if (hours <= 0)
            throw new DomainException(ErrorCodes.InvalidState, "時數必須大於 0。");
        return new Timesheet
        {
            Id = Guid.NewGuid(),
            LocalSlotId = localSlotId.Trim(),
            PersonId = personId,
            ProjectId = projectId,
            WorkDate = workDate,
            Hours = hours,
            Status = TimesheetStatus.PendingPm,
            IssueNumbers = issues.Where(n => n > 0).Distinct().ToList(),
            ContributionTypes = NormalizeTypes(contributionTypes),
            Chart = chart.ToList(),
            IsCorrection = isCorrection,
            CorrectsLocalSlotId = string.IsNullOrWhiteSpace(correctsLocalSlotId) ? null : correctsLocalSlotId.Trim(),
            UploadedAt = now,
        };
    }

    public void ReplacePending(Guid projectId, DateOnly workDate, decimal hours, IEnumerable<int> issues, IEnumerable<StatusChartCell> chart, DateTimeOffset now, IEnumerable<string>? contributionTypes = null)
    {
        if (Status == TimesheetStatus.Approved)
            throw new DomainException(ErrorCodes.TimesheetApprovedImmutable, Messages.TimesheetApprovedImmutable);
        ProjectId = projectId;
        WorkDate = workDate;
        Hours = hours;
        IssueNumbers = issues.Where(n => n > 0).Distinct().ToList();
        ContributionTypes = NormalizeTypes(contributionTypes);
        Chart = chart.ToList();
        Status = TimesheetStatus.PendingPm;
        ReturnReason = null;
        UploadedAt = now;
        ConfirmedAt = null;
    }

    static List<string> NormalizeTypes(IEnumerable<string>? types) =>
        (types ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .ToList();

    public void Confirm(DateTimeOffset now)
    {
        Status = TimesheetStatus.Approved;
        ReturnReason = null;
        ConfirmedAt = now;
    }

    public void ReturnToEngineer(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException(ErrorCodes.MissingReason, Messages.MissingReason);
        Status = TimesheetStatus.Returned;
        ReturnReason = reason.Trim();
        ConfirmedAt = null;
    }
}

public sealed class StatusChartCell
{
    public Guid ProjectId { get; private set; }
    public DateOnly Date { get; private set; }
    public int Intensity { get; private set; }
    public string? Note { get; private set; }

    public StatusChartCell(Guid projectId, DateOnly date, int intensity, string? note)
    {
        ProjectId = projectId;
        Date = date;
        Intensity = Math.Clamp(intensity, 0, 3);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
