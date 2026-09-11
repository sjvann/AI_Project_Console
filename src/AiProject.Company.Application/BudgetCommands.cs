using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class BudgetCommands
{
    readonly IProjectRepository _projects;
    readonly IContractRepository _contracts;
    readonly IClientRepository _clients;
    readonly ITimesheetRepository _timesheets;
    readonly IPayrollRepository _periods;
    readonly ISettingsRepository _settings;
    readonly IAuthorizationGate _auth;
    readonly IFieldEncryptor _encryptor;
    readonly IEnumerable<IRevenueRecognizer> _recognizers;
    readonly IUnitOfWork _uow;
    readonly IClock _clock;
    readonly ICurrentUser _user;

    public BudgetCommands(
        IProjectRepository projects,
        IContractRepository contracts,
        IClientRepository clients,
        ITimesheetRepository timesheets,
        IPayrollRepository periods,
        ISettingsRepository settings,
        IAuthorizationGate auth,
        IFieldEncryptor encryptor,
        IEnumerable<IRevenueRecognizer> recognizers,
        IUnitOfWork uow,
        IClock clock,
        ICurrentUser user)
    {
        _projects = projects;
        _contracts = contracts;
        _clients = clients;
        _timesheets = timesheets;
        _periods = periods;
        _settings = settings;
        _auth = auth;
        _encryptor = encryptor;
        _recognizers = recognizers;
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

    public async Task<ProjectPnl> PnlAsync(Guid projectId, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageBudget, projectId: projectId);
        if (!gate.Ok)
            throw new DomainException(gate.Code, gate.Message);
        var project = await _projects.GetAsync(projectId, ct) ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("專案"));
        return await BuildPnlAsync(project, ct);
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
            var pnl = await BuildPnlAsync(project, ct);
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

    async Task<ProjectPnl> BuildPnlAsync(Project project, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var today = _clock.Today(settings.TimeZoneId);
        var sheets = (await _timesheets.ListAsync(ct)).Where(t => t.ProjectId == project.Id).ToList();
        var approvedHours = sheets.Where(t => t.Status == TimesheetStatus.Approved).Sum(t => t.Hours);
        var contract = await _contracts.GetAsync(project.ContractId, ct);
        decimal clientRate = 0;
        if (!string.IsNullOrEmpty(project.ClientRateCipher) && _auth.Can(PlatformCapability.ViewClientRate, project.Id))
            clientRate = _encryptor.DecryptDecimal(project.ClientRateCipher);
        var recognizer = _recognizers.First(r => r.Kind == project.RevenueMethod switch
        {
            RevenueMethod.StraightLine => "straight-line",
            RevenueMethod.TimeAndMaterials => "tm",
            _ => "milestone",
        });
        var tmHours = project.RevenueMethod == RevenueMethod.TimeAndMaterials ? approvedHours * clientRate : approvedHours;
        var actualRevenue = recognizer.Recognize(project, today, sheets, tmHours);
        if (project.RevenueMethod == RevenueMethod.TimeAndMaterials)
            actualRevenue = decimal.Round(approvedHours * clientRate, 2);
        var plannedRevenue = contract?.Amount ?? project.Milestones.Sum(m => m.BillingAmount);
        if (contract is not null && contract.Currency != settings.Currency)
        {
            plannedRevenue = settings.ToCompanyCurrency(plannedRevenue, contract.Currency);
            actualRevenue = settings.ToCompanyCurrency(actualRevenue, contract.Currency);
        }
        var periods = await _periods.ListAsync(ct);
        var cost = periods.SelectMany(p => p.Lines).Where(l => l.ProjectId == project.Id).Sum(l => l.CostAmount);
        var otherPlanned = project.OtherExpenses.Sum(e => e.Planned);
        var otherActual = project.OtherExpenses.Where(e => e.IsActual).Sum(e => e.Actual);
        var margin = new MarginResult(plannedRevenue, actualRevenue, cost, cost, otherPlanned, otherActual);
        return new ProjectPnl(project.Id, project.Name, margin, settings.Classify(margin.ActualRate), approvedHours, clientRate);
    }
}

public sealed record ProjectPnl(Guid ProjectId, string Name, MarginResult Margin, string Tone, decimal ApprovedHours, decimal ClientRate);
