using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class BudgetCommands
{
    readonly IProjectRepository _projects;
    readonly IContractRepository _contracts;
    readonly IClientRepository _clients;
    readonly ITimesheetRepository _timesheets;
    readonly IPayrollRepository _periods;
    readonly IPersonRepository _people;
    readonly IAssignmentRepository _assignments;
    readonly ISettingsRepository _settings;
    readonly IAuthorizationGate _auth;
    readonly IFieldEncryptor _encryptor;
    readonly IEnumerable<IRevenueRecognizer> _recognizers;
    readonly MonthlySalaryCalculator _monthly;
    readonly HourlyCalculator _hourly;
    readonly IUnitOfWork _uow;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public BudgetCommands(
        IProjectRepository projects,
        IContractRepository contracts,
        IClientRepository clients,
        ITimesheetRepository timesheets,
        IPayrollRepository periods,
        IPersonRepository people,
        IAssignmentRepository assignments,
        ISettingsRepository settings,
        IAuthorizationGate auth,
        IFieldEncryptor encryptor,
        IEnumerable<IRevenueRecognizer> recognizers,
        MonthlySalaryCalculator monthly,
        HourlyCalculator hourly,
        IUnitOfWork uow,
        IClock clock,
        ICurrentUser user)
    {
        _projects = projects;
        _contracts = contracts;
        _clients = clients;
        _timesheets = timesheets;
        _periods = periods;
        _people = people;
        _assignments = assignments;
        _settings = settings;
        _auth = auth;
        _encryptor = encryptor;
        _recognizers = recognizers;
        _monthly = monthly;
        _hourly = hourly;
        _uow = uow;
        _clock = clock;
        _user = user;
    }

    public async Task<Outcome> RecognizeAsync(Guid projectId, Guid milestoneId, RecognitionStep step, bool oneClick, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.RecognizeRevenue, projectId: projectId);
        if (!gate.Ok)
            return gate;
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var milestone = project.Milestones.FirstOrDefault(m => m.Id == milestoneId);
        if (milestone is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("里程碑"));
        milestone.SetRecognition(oneClick ? RecognitionStep.Collected : step);
        var actor = string.IsNullOrWhiteSpace(_user.DisplayName) ? _user.UserName : _user.DisplayName;
        project.AddJournal(_clock.UtcNow, actor, ProjectJournalKind.Recognized, $"認列 {milestone.Name}");
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> AddExpenseAsync(Guid projectId, string label, decimal planned, decimal actual, bool isActual, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageBudget, projectId: projectId);
        if (!gate.Ok)
            return gate;
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        project.AddOtherExpense(label, planned, actual, isActual);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<ProjectPnl> PnlAsync(Guid projectId, int? year = null, int? month = null, CancellationToken ct = default)
    {
        // 戰情室只讀（ViewWarRoom）與預算編輯（ManageBudget）皆可讀損益。
        if (!_auth.Can(PlatformCapability.ManageBudget, projectId: projectId)
            && !_auth.Can(PlatformCapability.ViewWarRoom))
        {
            var gate = _auth.Ensure(PlatformCapability.ManageBudget, projectId: projectId);
            throw new DomainException(gate.Code, gate.Message);
        }
        var project = await _projects.GetAsync(projectId, ct) ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("專案"));
        return await BuildPnlAsync(project, year, month, ct);
    }

    public async Task<IReadOnlyList<CostAllocationRow>> AllocationForMonthAsync(int year, int month, Guid? projectId = null, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageBudget, projectId: projectId);
        if (!gate.Ok)
            throw new DomainException(gate.Code, gate.Message);
        var (start, end) = MonthRange(year, month);
        var people = (await _people.ListAsync(ct)).ToDictionary(p => p.Id);
        var projects = (await _projects.ListAsync(ct)).ToDictionary(p => p.Id);
        var sheets = (await _timesheets.ListForPeriodAsync(start, end, ct)).ToList();
        var assignments = await _assignments.ListAsync(ct);
        var rows = new List<CostAllocationRow>();

        foreach (var person in people.Values.Where(p => !p.IsDeleted))
        {
            if (person.CostAllocationExempt)
                continue;
            var mineApproved = sheets.Where(t => t.PersonId == person.Id && t.Status == TimesheetStatus.Approved).ToList();
            var mineEstimate = sheets.Where(t => t.PersonId == person.Id && t.Status != TimesheetStatus.Returned).ToList();

            if (person.EmploymentKind == EmploymentKind.FullTime && !string.IsNullOrEmpty(person.MonthlySalaryCipher))
            {
                var salary = _encryptor.DecryptDecimal(person.MonthlySalaryCipher);
                var useSheets = mineApproved.Count > 0 ? mineApproved : mineEstimate;
                var allocated = _monthly.AllocateCost(person, salary, useSheets, assignments, start, end);
                var personTotal = allocated.Sum(l => l.CostAmount);
                foreach (var line in allocated.Where(l => l.ProjectId is Guid pid && (projectId is null || pid == projectId)))
                {
                    projects.TryGetValue(line.ProjectId!.Value, out var project);
                    var weight = personTotal <= 0 ? 0 : decimal.Round(line.CostAmount / personTotal * 100m, 1);
                    var source = mineApproved.Count > 0 ? "核准工時" : (mineEstimate.Count > 0 ? "已送工時" : "派工規劃");
                    rows.Add(new CostAllocationRow(
                        person.Id,
                        person.DisplayName,
                        line.ProjectId.Value,
                        project?.Name ?? line.ProjectId.Value.ToString()[..8],
                        line.Hours,
                        line.CostAmount,
                        weight,
                        source));
                }
            }
            else if (!string.IsNullOrEmpty(person.InternalHourlyRateCipher))
            {
                var rate = _encryptor.DecryptDecimal(person.InternalHourlyRateCipher);
                var useSheets = mineApproved.Count > 0 ? mineApproved : mineEstimate;
                var allocated = _hourly.AllocateCost(person, rate, useSheets);
                if (allocated.Count == 0 && mineEstimate.Count == 0)
                {
                    // 無工時時用派工週時粗估
                    var planned = assignments
                        .Where(a => a.PersonId == person.Id && a.Overlaps(start, end) && a.Status != AssignmentStatus.Cancelled)
                        .GroupBy(a => a.ProjectId)
                        .Select(g => (ProjectId: g.Key, Hours: g.Sum(a => a.PlannedHoursPerWeek)));
                    foreach (var item in planned.Where(p => projectId is null || p.ProjectId == projectId))
                    {
                        projects.TryGetValue(item.ProjectId, out var project);
                        rows.Add(new CostAllocationRow(
                            person.Id,
                            person.DisplayName,
                            item.ProjectId,
                            project?.Name ?? item.ProjectId.ToString()[..8],
                            item.Hours,
                            decimal.Round(item.Hours * rate, 2),
                            100m,
                            "派工規劃"));
                    }
                    continue;
                }
                var personTotal = allocated.Sum(l => l.CostAmount);
                foreach (var line in allocated.Where(l => l.ProjectId is Guid pid && (projectId is null || pid == projectId)))
                {
                    projects.TryGetValue(line.ProjectId!.Value, out var project);
                    var weight = personTotal <= 0 ? 0 : decimal.Round(line.CostAmount / personTotal * 100m, 1);
                    rows.Add(new CostAllocationRow(
                        person.Id,
                        person.DisplayName,
                        line.ProjectId.Value,
                        project?.Name ?? line.ProjectId.Value.ToString()[..8],
                        line.Hours,
                        line.CostAmount,
                        weight,
                        mineApproved.Count > 0 ? "核准工時" : "已送工時"));
                }
            }
        }

        return rows
            .OrderByDescending(r => r.CostAmount)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Outcome<string>> ExportMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageBudget);
        if (!gate.Ok)
            return Outcome<string>.Fail(gate.Code, gate.Message);
        var projects = await _projects.ListAsync(ct);
        var rows = new List<BudgetCsvRow>();
        var settings = await _settings.GetAsync(ct);
        foreach (var project in projects.Where(p => !p.IsDeleted))
        {
            var pnl = await BuildPnlAsync(project, year, month, ct);
            var contract = await _contracts.GetAsync(project.ContractId, ct);
            var client = contract is null ? null : await _clients.GetAsync(contract.ClientId, ct);
            rows.Add(new BudgetCsvRow(
                project.Id, project.Name, client?.Name ?? "",
                pnl.Margin.PlannedRevenue, pnl.Margin.ActualRevenue,
                pnl.Margin.PlannedCost, pnl.Margin.ActualCost,
                pnl.Margin.OtherPlanned, pnl.Margin.OtherActual,
                pnl.Margin.PlannedMargin, pnl.Margin.ActualMargin,
                MarginResult.Display(pnl.Margin.PlannedRate),
                MarginResult.Display(pnl.Margin.ActualRate),
                settings.Currency));
        }
        return Outcome<string>.Success(BudgetCsv.Render(rows));
    }

    public async Task<Outcome<string>> ExportAllocationAsync(int year, int month, Guid? projectId = null, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageBudget, projectId: projectId);
        if (!gate.Ok)
            return Outcome<string>.Fail(gate.Code, gate.Message);
        var rows = await AllocationForMonthAsync(year, month, projectId, ct);
        return Outcome<string>.Success(CostAllocationCsv.Render(rows));
    }

    async Task<ProjectPnl> BuildPnlAsync(Project project, int? year, int? month, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var today = _clock.Today(settings.TimeZoneId);
        DateOnly? rangeStart = null;
        DateOnly? rangeEnd = null;
        if (year is int y && month is int m)
            (rangeStart, rangeEnd) = MonthRange(y, m);

        var allSheets = (await _timesheets.ListAsync(ct)).Where(t => t.ProjectId == project.Id).ToList();
        var sheetsInRange = rangeStart is DateOnly rs && rangeEnd is DateOnly re
            ? allSheets.Where(t => t.WorkDate >= rs && t.WorkDate <= re).ToList()
            : allSheets;
        var approvedHours = sheetsInRange.Where(t => t.Status == TimesheetStatus.Approved).Sum(t => t.Hours);
        var contract = await _contracts.GetAsync(project.ContractId, ct);
        decimal clientRate = 0;
        var canSeeClientRate = _auth.Can(PlatformCapability.ViewClientRate, project.Id);
        // 毛利核算需要對客戶費率；UI 是否顯示另看 ViewClientRate。
        if (!string.IsNullOrEmpty(project.ClientRateCipher))
            clientRate = _encryptor.DecryptDecimal(project.ClientRateCipher);
        var recognizer = _recognizers.First(r => r.Kind == project.RevenueMethod switch
        {
            RevenueMethod.StraightLine => "straight-line",
            RevenueMethod.TimeAndMaterials => "tm",
            _ => "milestone",
        });
        var tmHours = project.RevenueMethod == RevenueMethod.TimeAndMaterials ? approvedHours * clientRate : approvedHours;
        var asOf = rangeEnd ?? today;
        var actualRevenue = recognizer.Recognize(project, asOf, sheetsInRange, tmHours);
        if (project.RevenueMethod == RevenueMethod.TimeAndMaterials)
            actualRevenue = decimal.Round(approvedHours * clientRate, 2);
        var plannedRevenue = contract?.Amount ?? project.Milestones.Sum(m => m.BillingAmount);
        if (contract is not null && contract.Currency != settings.Currency)
        {
            plannedRevenue = settings.ToCompanyCurrency(plannedRevenue, contract.Currency);
            actualRevenue = settings.ToCompanyCurrency(actualRevenue, contract.Currency);
        }

        var periods = await _periods.ListAsync(ct);
        var periodLines = periods
            .Where(p => rangeStart is null || p.Overlaps(rangeStart.Value, rangeEnd!.Value))
            .SelectMany(p => p.Lines)
            .Where(l => l.ProjectId == project.Id);
        var actualCost = periodLines.Sum(l => l.CostAmount);
        var plannedCost = await EstimatePlannedLaborCostAsync(project.Id, rangeStart, rangeEnd, ct);
        if (actualCost <= 0 && plannedCost > 0)
        {
            // 尚無薪資週期入帳時，用預估當實際口徑的下限（僅核准工時／已分攤）。
            var approvedOnly = await EstimatePlannedLaborCostAsync(project.Id, rangeStart, rangeEnd, ct, approvedOnly: true);
            actualCost = approvedOnly;
        }

        var otherPlanned = project.OtherExpenses.Sum(e => e.Planned);
        var otherActual = project.OtherExpenses.Where(e => e.IsActual).Sum(e => e.Actual);
        var margin = new MarginResult(plannedRevenue, actualRevenue, plannedCost, actualCost, otherPlanned, otherActual);
        return new ProjectPnl(
            project.Id,
            project.Name,
            margin,
            settings.Classify(margin.ActualRate),
            approvedHours,
            canSeeClientRate ? clientRate : 0,
            canSeeClientRate);
    }

    async Task<decimal> EstimatePlannedLaborCostAsync(
        Guid projectId,
        DateOnly? rangeStart,
        DateOnly? rangeEnd,
        CancellationToken ct,
        bool approvedOnly = false)
    {
        var people = await _people.ListAsync(ct);
        var assignments = await _assignments.ListAsync(ct);
        IReadOnlyList<Timesheet> sheets;
        if (rangeStart is DateOnly rs && rangeEnd is DateOnly re)
            sheets = await _timesheets.ListForPeriodAsync(rs, re, ct);
        else
            sheets = await _timesheets.ListAsync(ct);

        var start = rangeStart ?? sheets.Select(t => t.WorkDate).DefaultIfEmpty(_clock.Today("Asia/Taipei")).Min();
        var end = rangeEnd ?? sheets.Select(t => t.WorkDate).DefaultIfEmpty(start).Max();
        decimal total = 0;
        foreach (var person in people.Where(p => !p.IsDeleted))
        {
            if (person.CostAllocationExempt)
                continue;
            var mineAll = sheets.Where(t => t.PersonId == person.Id).ToList();
            var mine = approvedOnly
                ? mineAll.Where(t => t.Status == TimesheetStatus.Approved).ToList()
                : mineAll.Where(t => t.Status != TimesheetStatus.Returned).ToList();

            if (person.EmploymentKind == EmploymentKind.FullTime && !string.IsNullOrEmpty(person.MonthlySalaryCipher))
            {
                var salary = _encryptor.DecryptDecimal(person.MonthlySalaryCipher);
                var allocated = _monthly.AllocateCost(person, salary, mine, assignments, start, end);
                total += allocated.Where(l => l.ProjectId == projectId).Sum(l => l.CostAmount);
            }
            else if (!string.IsNullOrEmpty(person.InternalHourlyRateCipher))
            {
                var rate = _encryptor.DecryptDecimal(person.InternalHourlyRateCipher);
                var allocated = _hourly.AllocateCost(person, rate, mine);
                var projectCost = allocated.Where(l => l.ProjectId == projectId).Sum(l => l.CostAmount);
                if (projectCost > 0)
                    total += projectCost;
                else if (!approvedOnly && mine.Count == 0)
                {
                    var hours = assignments
                        .Where(a => a.PersonId == person.Id && a.ProjectId == projectId
                            && a.Overlaps(start, end) && a.Status != AssignmentStatus.Cancelled)
                        .Sum(a => a.PlannedHoursPerWeek);
                    total += decimal.Round(hours * rate, 2);
                }
            }
        }
        return total;
    }

    static (DateOnly Start, DateOnly End) MonthRange(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return (start, start.AddMonths(1).AddDays(-1));
    }
}

public sealed record ProjectPnl(
    Guid ProjectId,
    string Name,
    MarginResult Margin,
    string Tone,
    decimal ApprovedHours,
    decimal ClientRate,
    bool ClientRateVisible);
