namespace AiProject.Company.Domain;

public sealed class MonthlySalaryCalculator : IPayrollCalculator
{
    public string Kind => "monthly";

    public bool AppliesTo(Person person, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses) =>
        person.EmploymentKind == EmploymentKind.FullTime && !string.IsNullOrEmpty(person.MonthlySalaryCipher);

    public PayrollLine Calculate(Person person, CompanySettings settings, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses, DateOnly periodStart, DateOnly periodEnd)
    {
        return new PayrollLine(person.Id, PayrollLineKind.Monthly, 0, 0, null, 0, "月薪（金額由人資解密後寫入）", true);
    }

    public PayrollLine CalculateWithSalary(Person person, decimal monthlySalary, IReadOnlyList<Timesheet> approved)
    {
        var totalHours = approved.Where(t => t.PersonId == person.Id && t.Status == TimesheetStatus.Approved).Sum(t => t.Hours);
        return new PayrollLine(person.Id, PayrollLineKind.Monthly, monthlySalary, monthlySalary, null, totalHours, "月薪實發與月薪一致；專案分攤只影響成本", true);
    }

    public IReadOnlyList<PayrollLine> AllocateCost(Person person, decimal monthlySalary, IReadOnlyList<Timesheet> approved, IReadOnlyList<Assignment> assignments, DateOnly periodStart, DateOnly periodEnd)
    {
        if (person.CostAllocationExempt)
            return [];
        // 呼叫端決定口徑（核准／已送）；此處只依傳入列分攤。
        var mine = approved.Where(t => t.PersonId == person.Id).ToList();
        var byProject = mine.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.Sum(x => x.Hours));
        if (byProject.Count == 0)
        {
            var planned = assignments
                .Where(a => a.PersonId == person.Id && a.Overlaps(periodStart, periodEnd) && a.Status != AssignmentStatus.Cancelled)
                .GroupBy(a => a.ProjectId)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.PlannedHoursPerWeek));
            byProject = planned;
        }
        var total = byProject.Values.Sum();
        if (total <= 0)
            return [];
        return byProject.Select(kv =>
            new PayrollLine(person.Id, PayrollLineKind.Monthly, 0, decimal.Round(monthlySalary * kv.Value / total, 2), kv.Key, kv.Value, "正職成本分攤", false)).ToList();
    }
}

public sealed class HourlyCalculator : IPayrollCalculator
{
    public string Kind => "hourly";

    public bool AppliesTo(Person person, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses) =>
        person.EmploymentKind is EmploymentKind.Freelance or EmploymentKind.VendorStaff
        || !string.IsNullOrEmpty(person.InternalHourlyRateCipher);

    public PayrollLine Calculate(Person person, CompanySettings settings, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses, DateOnly periodStart, DateOnly periodEnd) =>
        new(person.Id, PayrollLineKind.Hourly, 0, 0, null, approved.Where(t => t.PersonId == person.Id && t.Status == TimesheetStatus.Approved).Sum(t => t.Hours), "時計（金額依費率）", true);

    public (PayrollLine Payable, PayrollLine? Overtime) CalculateWithRate(Person person, decimal hourlyRate, IReadOnlyList<Timesheet> approved, decimal? dailyCapHours)
    {
        var mine = approved.Where(t => t.PersonId == person.Id && t.Status == TimesheetStatus.Approved).ToList();
        var cap = dailyCapHours ?? 8m;
        decimal payableHours = 0;
        decimal overtimeHours = 0;
        foreach (var day in mine.GroupBy(t => t.WorkDate))
        {
            var hours = day.Sum(t => t.Hours);
            if (hours > cap)
            {
                payableHours += cap;
                overtimeHours += hours - cap;
            }
            else
                payableHours += hours;
        }
        var payable = new PayrollLine(person.Id, PayrollLineKind.Hourly, decimal.Round(payableHours * hourlyRate, 2), decimal.Round(payableHours * hourlyRate, 2), null, payableHours, "核准小時 × 費率", true);
        PayrollLine? overtime = overtimeHours > 0
            ? new PayrollLine(person.Id, PayrollLineKind.OvertimePending, 0, 0, null, overtimeHours, "超過每日上限，待人資核准金額", false)
            : null;
        return (payable, overtime);
    }

    /// <summary>依專案拆成本列（實發仍用 CalculateWithRate 的無專案列）。</summary>
    public IReadOnlyList<PayrollLine> AllocateCost(Person person, decimal hourlyRate, IReadOnlyList<Timesheet> sheets)
    {
        var mine = sheets.Where(t => t.PersonId == person.Id).ToList();
        if (mine.Count == 0)
            return [];
        return mine
            .GroupBy(t => t.ProjectId)
            .Select(g =>
            {
                var hours = g.Sum(t => t.Hours);
                return new PayrollLine(
                    person.Id,
                    PayrollLineKind.Hourly,
                    0,
                    decimal.Round(hours * hourlyRate, 2),
                    g.Key,
                    hours,
                    "時計成本分攤",
                    false);
            })
            .ToList();
    }
}

public sealed class ProjectBonusCalculator : IPayrollCalculator
{
    public string Kind => "project-bonus";

    public bool AppliesTo(Person person, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses) =>
        bonuses.Any(b => b.PersonId == person.Id);

    public PayrollLine Calculate(Person person, CompanySettings settings, IReadOnlyList<Timesheet> approved, IReadOnlyList<ProjectBonusGrant> bonuses, DateOnly periodStart, DateOnly periodEnd)
    {
        var payable = bonuses.Where(b => b.PersonId == person.Id && b.Payable).Sum(b => b.Amount);
        return new PayrollLine(person.Id, PayrollLineKind.ProjectBonus, payable, payable, null, 0, payable > 0 ? "可發專案獎金" : Messages.BonusNotPayable, payable > 0);
    }

    public IReadOnlyList<PayrollLine> Expand(Person person, IReadOnlyList<ProjectBonusGrant> bonuses) =>
        bonuses.Where(b => b.PersonId == person.Id).Select(b =>
            new PayrollLine(person.Id, PayrollLineKind.ProjectBonus, b.Payable ? b.Amount : 0, b.Payable ? b.Amount : 0, b.ProjectId, 0,
                b.Payable ? "可發專案獎金" : Messages.BonusNotPayable, b.Payable)).ToList();
}

public sealed class MilestoneRevenueRecognizer : IRevenueRecognizer
{
    public string Kind => "milestone";

    public decimal Recognize(Project project, DateOnly asOf, IReadOnlyList<Timesheet> approved, decimal billableHours) =>
        project.Milestones.Where(m => m.Recognition >= RecognitionStep.Invoiced).Sum(m => m.BillingAmount);
}

public sealed class StraightLineRevenueRecognizer : IRevenueRecognizer
{
    public string Kind => "straight-line";

    public decimal Recognize(Project project, DateOnly asOf, IReadOnlyList<Timesheet> approved, decimal billableHours)
    {
        var days = Math.Max(1, project.TargetEnd.DayNumber - project.Start.DayNumber + 1);
        var elapsed = Math.Clamp(asOf.DayNumber - project.Start.DayNumber + 1, 0, days);
        var total = project.Milestones.Sum(m => m.BillingAmount);
        return decimal.Round(total * elapsed / days, 2);
    }
}

public sealed class TimeAndMaterialsRevenueRecognizer : IRevenueRecognizer
{
    public string Kind => "tm";

    public decimal Recognize(Project project, DateOnly asOf, IReadOnlyList<Timesheet> approved, decimal billableHours) =>
        decimal.Round(billableHours, 2);
}
