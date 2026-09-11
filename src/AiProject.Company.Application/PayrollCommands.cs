using AiProject.Company.Contracts;
using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class PayrollCommands
{
    readonly IPayrollRepository _periods;
    readonly IPersonRepository _people;
    readonly ITimesheetRepository _timesheets;
    readonly IAssignmentRepository _assignments;
    readonly IProjectRepository _projects;
    readonly IAuthorizationGate _auth;
    readonly IFieldEncryptor _encryptor;
    readonly MonthlySalaryCalculator _monthly;
    readonly HourlyCalculator _hourly;
    readonly ProjectBonusCalculator _bonus;
    readonly IUnitOfWork _uow;
    readonly AuditWriter _audit;
    readonly IClock _clock;

    public PayrollCommands(
        IPayrollRepository periods,
        IPersonRepository people,
        ITimesheetRepository timesheets,
        IAssignmentRepository assignments,
        IProjectRepository projects,
        IAuthorizationGate auth,
        IFieldEncryptor encryptor,
        MonthlySalaryCalculator monthly,
        HourlyCalculator hourly,
        ProjectBonusCalculator bonus,
        IUnitOfWork uow,
        AuditWriter audit,
        IClock clock)
    {
        _periods = periods;
        _people = people;
        _timesheets = timesheets;
        _assignments = assignments;
        _projects = projects;
        _auth = auth;
        _encryptor = encryptor;
        _monthly = monthly;
        _hourly = hourly;
        _bonus = bonus;
        _uow = uow;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Outcome<Guid>> OpenAsync(DateOnly start, DateOnly end, Guid? correctsId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.LockPayroll);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var period = PayrollPeriod.Open(start, end, correctsId);
        await _periods.AddAsync(period, ct);
        await RecalculateAsync(period, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(period.Id);
    }

    public async Task<Outcome> RecalculateAsync(Guid periodId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.LockPayroll);
        if (!gate.Ok)
            return gate;
        var period = await _periods.GetAsync(periodId, ct);
        if (period is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("薪資週期"));
        await RecalculateAsync(period, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> LockAsync(Guid periodId, string reason, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.LockPayroll);
        if (!gate.Ok)
            return gate;
        if (string.IsNullOrWhiteSpace(reason))
            return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
        var period = await _periods.GetAsync(periodId, ct);
        if (period is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("薪資週期"));
        period.Lock(_clock.UtcNow);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.LockPayroll, "PayrollPeriod", period.Id, reason, PayrollPeriodStatus.PmConfirming, PayrollPeriodStatus.Locked, ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetOvertimeAsync(Guid periodId, Guid lineId, decimal amount, string reason, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.LockPayroll);
        if (!gate.Ok)
            return gate;
        if (string.IsNullOrWhiteSpace(reason))
            return Outcome.Fail(ErrorCodes.MissingReason, Messages.MissingReason);
        var period = await _periods.GetAsync(periodId, ct);
        if (period is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("薪資週期"));
        period.SetOvertimeAmount(lineId, amount);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.ChangeRate, "PayrollLine", lineId, reason, 0, amount, ct);
        return Outcome.Success();
    }

    public async Task<Outcome<string>> ExportCsvAsync(Guid periodId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.LockPayroll);
        if (!gate.Ok)
            return Outcome<string>.Fail(gate.Code, gate.Message);
        var period = await _periods.GetAsync(periodId, ct);
        if (period is null)
            return Outcome<string>.Fail(ErrorCodes.NotFound, Messages.NotFound("薪資週期"));
        if (period.Status != PayrollPeriodStatus.Locked)
            return Outcome<string>.Fail(ErrorCodes.InvalidState, "鎖定後才能匯出 CSV。");
        var people = (await _people.ListAsync(ct)).ToDictionary(p => p.Id);
        var rows = period.Lines.Select(l => (l, people.TryGetValue(l.PersonId, out var p) ? p.DisplayName : l.PersonId.ToString()));
        return Outcome<string>.Success(PayrollCsv.Render(rows));
    }

    public async Task<PayslipDto> PayslipAsync(Guid personId, Guid periodId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ViewOwnPayslip, personId: personId);
        if (!gate.Ok)
            throw new DomainException(gate.Code, gate.Message);
        var salaryGate = _auth.Ensure(PlatformCapability.ViewSalary, personId: personId);
        if (!salaryGate.Ok)
            throw new DomainException(salaryGate.Code, salaryGate.Message);
        await _audit.SensitiveRead(AuditActions.ViewPayslip, "PayrollPeriod", periodId, ct);
        var period = await _periods.GetAsync(periodId, ct) ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("薪資週期"));
        var lines = period.Lines.Where(l => l.PersonId == personId && l.Payable).Select(l =>
            new PayslipLineDto { Kind = l.Kind.ToString(), Amount = l.Amount, Hours = l.Hours, Note = l.Note, ProjectId = l.ProjectId }).ToList();
        return new PayslipDto
        {
            PeriodStart = period.Start,
            PeriodEnd = period.End,
            PeriodStatus = period.Status.ToString(),
            PayableTotal = lines.Sum(l => l.Amount),
            Lines = lines,
        };
    }

    async Task RecalculateAsync(PayrollPeriod period, CancellationToken ct)
    {
        period.EnsureUnlocked();
        var people = await _people.ListAsync(ct);
        var sheets = (await _timesheets.ListForPeriodAsync(period.Start, period.End, ct)).Where(t => t.Status == TimesheetStatus.Approved).ToList();
        var assignments = await _assignments.ListAsync(ct);
        var projects = await _projects.ListAsync(ct);
        var bonuses = new List<ProjectBonusGrant>();
        foreach (var project in projects)
        {
            foreach (var milestone in project.Milestones.Where(m => m.BonusPayable))
            {
                foreach (var assignment in assignments.Where(a => a.ProjectId == project.Id && a.Status != AssignmentStatus.Cancelled))
                    bonuses.Add(new ProjectBonusGrant(assignment.PersonId, project.Id, 0, true));
            }
        }
        var lines = new List<PayrollLine>();
        foreach (var person in people.Where(p => !p.IsDeleted))
        {
            var mine = sheets.Where(t => t.PersonId == person.Id).ToList();
            var personBonuses = bonuses.Where(b => b.PersonId == person.Id).ToList();
            if (person.EmploymentKind == EmploymentKind.FullTime && !string.IsNullOrEmpty(person.MonthlySalaryCipher))
            {
                var salary = _encryptor.DecryptDecimal(person.MonthlySalaryCipher);
                lines.Add(_monthly.CalculateWithSalary(person, salary, mine));
                lines.AddRange(_monthly.AllocateCost(person, salary, mine, assignments, period.Start, period.End));
            }
            if ((person.EmploymentKind is EmploymentKind.Freelance or EmploymentKind.VendorStaff || !string.IsNullOrEmpty(person.InternalHourlyRateCipher))
                && !string.IsNullOrEmpty(person.InternalHourlyRateCipher))
            {
                var rate = _encryptor.DecryptDecimal(person.InternalHourlyRateCipher);
                var (payable, overtime) = _hourly.CalculateWithRate(person, rate, mine, 8);
                lines.Add(payable);
                if (overtime is not null)
                    lines.Add(overtime);
            }
            lines.AddRange(_bonus.Expand(person, personBonuses));
        }
        period.ReplaceLines(lines);
        period.MarkPmConfirming();
    }
}
