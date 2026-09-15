namespace AiProject.Company.Domain;

public enum PayrollPeriodStatus
{
    Collecting = 0,
    PmConfirming = 1,
    Locked = 2,
}

public enum PayrollLineKind
{
    Monthly = 0,
    Hourly = 1,
    ProjectBonus = 2,
    OvertimePending = 3,
}

public sealed class PayrollPeriod : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public PayrollPeriodStatus Status { get; private set; }
    public Guid? CorrectsPeriodId { get; private set; }
    public DateTimeOffset? LockedAt { get; private set; }
    public List<PayrollLine> Lines { get; private set; } = [];

    public static PayrollPeriod Open(DateOnly start, DateOnly end, Guid? correctsPeriodId = null)
    {
        if (end < start)
            throw new DomainException(ErrorCodes.InvalidState, "週期結束日不能早於開始日。");
        return new PayrollPeriod
        {
            Id = Guid.NewGuid(),
            Start = start,
            End = end,
            Status = PayrollPeriodStatus.Collecting,
            CorrectsPeriodId = correctsPeriodId,
        };
    }

    public void MarkPmConfirming()
    {
        EnsureUnlocked();
        Status = PayrollPeriodStatus.PmConfirming;
    }

    public void ReplaceLines(IEnumerable<PayrollLine> lines)
    {
        EnsureUnlocked();
        Lines = lines.ToList();
    }

    public void SetOvertimeAmount(Guid lineId, decimal amount)
    {
        EnsureUnlocked();
        var line = Lines.FirstOrDefault(l => l.Id == lineId)
            ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("加班列"));
        line.SetAmount(amount);
    }

    public void Lock(DateTimeOffset now)
    {
        EnsureUnlocked();
        Status = PayrollPeriodStatus.Locked;
        LockedAt = now;
    }

    public void EnsureUnlocked()
    {
        if (Status == PayrollPeriodStatus.Locked)
            throw new DomainException(ErrorCodes.PayrollLocked, Messages.PayrollLocked);
    }
}

public sealed class PayrollLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }
    public PayrollLineKind Kind { get; private set; }
    public decimal Amount { get; private set; }
    public decimal CostAmount { get; private set; }
    public Guid? ProjectId { get; private set; }
    public decimal Hours { get; private set; }
    public string Note { get; private set; } = "";
    public bool Payable { get; private set; }

    public PayrollLine(Guid personId, PayrollLineKind kind, decimal amount, decimal costAmount, Guid? projectId, decimal hours, string note, bool payable)
    {
        PersonId = personId;
        Kind = kind;
        Amount = amount;
        CostAmount = costAmount;
        ProjectId = projectId;
        Hours = hours;
        Note = note;
        Payable = payable;
    }

    public void SetAmount(decimal amount)
    {
        Amount = amount;
        if (Kind == PayrollLineKind.OvertimePending)
            Payable = amount > 0;
    }
}

public sealed record ProjectBonusGrant(Guid PersonId, Guid ProjectId, decimal Amount, bool Payable);

public static class PayrollCsv
{
    public const string Header = "personId,displayName,kind,amount,costAmount,projectId,hours,payable,note";

    public static string Render(IEnumerable<(PayrollLine Line, string DisplayName)> rows)
    {
        var output = new System.Text.StringBuilder();
        output.AppendLine(Header);
        foreach (var (line, name) in rows)
        {
            output.Append(line.PersonId).Append(',')
                .Append(Csv(name)).Append(',')
                .Append(line.Kind).Append(',')
                .Append(line.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(line.CostAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(line.ProjectId).Append(',')
                .Append(line.Hours.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                .Append(line.Payable).Append(',')
                .Append(Csv(line.Note))
                .AppendLine();
        }
        return output.ToString();
    }

    static string Csv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
