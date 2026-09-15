using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

public class BudgetCommandsTests
{
    [Fact]
    public async Task Milestone_recognize_and_labor_cost_explain_kpi05()
    {
        var h = Harness();
        var person = Person.Create("王工程", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers("80000", null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedProjectAsync(h, RevenueMethod.Milestone, contractAmount: 200000);
        var ms = project.AddMilestone("交付", new DateOnly(2026, 9, 15), 100000, null);

        var sheet = Timesheet.Upload("slot-b1", person.Id, project.Id, new DateOnly(2026, 9, 3), 40, [], [], false, null, DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);
        Assert.True((await h.TimesheetsCmd.ConfirmAsync(sheet.Id)).Ok);

        var opened = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.True(opened.Ok, opened.Message);
        var period = h.Periods.Items.Single();
        Assert.Contains(period.Lines, l => l.ProjectId == project.Id && l.CostAmount == 80000 && !l.Payable);

        var before = await h.Budget.PnlAsync(project.Id, 2026, 9);
        Assert.Equal(0, before.Margin.ActualRevenue);
        Assert.Equal(80000, before.Margin.ActualCost);

        var recognize = await h.Budget.RecognizeAsync(project.Id, ms.Id, RecognitionStep.Collected, true);
        Assert.True(recognize.Ok, recognize.Message);
        var after = await h.Budget.PnlAsync(project.Id, 2026, 9);
        Assert.Equal(100000, after.Margin.ActualRevenue);
        Assert.Equal(80000, after.Margin.ActualCost);
        Assert.Equal(20000, after.Margin.ActualMargin);

        var alloc = await h.Budget.AllocationForMonthAsync(2026, 9, project.Id);
        Assert.Single(alloc);
        Assert.Equal(80000, alloc[0].CostAmount);
        Assert.Equal(100m, alloc[0].WeightPercent);
        Assert.Equal("核准工時", alloc[0].Source);
    }

    [Fact]
    public async Task Tm_revenue_uses_client_rate_hourly_cost_by_project()
    {
        var h = Harness();
        var person = Person.Create("外包", EmploymentKind.Freelance, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers(null, "1000");
        await h.People.AddAsync(person);
        var (project, _) = await SeedProjectAsync(h, RevenueMethod.TimeAndMaterials, contractAmount: 0);
        project.Update(project.Name, project.Start, project.TargetEnd, RevenueMethod.TimeAndMaterials, null, false, ((IFieldEncryptor)h.Encryptor).EncryptDecimal(2500));

        var sheet = Timesheet.Upload("tm1", person.Id, project.Id, new DateOnly(2026, 9, 2), 8, [], [], false, null, DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);
        Assert.True((await h.TimesheetsCmd.ConfirmAsync(sheet.Id)).Ok);

        var opened = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.True(opened.Ok, opened.Message);
        Assert.Contains(h.Periods.Items[0].Lines, l => l.ProjectId == project.Id && l.CostAmount == 8000 && !l.Payable);

        var pnl = await h.Budget.PnlAsync(project.Id, 2026, 9);
        Assert.Equal(20000, pnl.Margin.ActualRevenue); // 8h × 2500
        Assert.Equal(8000, pnl.Margin.ActualCost);
        Assert.True(pnl.ClientRateVisible);
        Assert.Equal(2500, pnl.ClientRate);
    }

    [Fact]
    public async Task Exempt_staff_excluded_from_allocation_export()
    {
        var h = Harness();
        var manager = Person.Create("管理", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        manager.SetCompensationCiphers("90000", null);
        manager.SetCostAllocationExempt(true);
        await h.People.AddAsync(manager);
        var (project, _) = await SeedProjectAsync(h, RevenueMethod.Milestone, 50000);
        var sheet = Timesheet.Upload("mgr", manager.Id, project.Id, new DateOnly(2026, 9, 1), 8, [], [], false, null, DateTimeOffset.UtcNow);
        sheet.Confirm(DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);

        var alloc = await h.Budget.AllocationForMonthAsync(2026, 9, project.Id);
        Assert.Empty(alloc);
        var csv = await h.Budget.ExportAllocationAsync(2026, 9);
        Assert.True(csv.Ok, csv.Message);
        Assert.StartsWith(CostAllocationCsv.Header, csv.Value);
        Assert.DoesNotContain(manager.Id.ToString(), csv.Value);
    }

    [Fact]
    public async Task Export_month_uses_stable_budget_header()
    {
        var h = Harness();
        await SeedProjectAsync(h, RevenueMethod.Milestone, 10000);
        var csv = await h.Budget.ExportMonthAsync(2026, 9);
        Assert.True(csv.Ok, csv.Message);
        Assert.StartsWith(BudgetCsv.Header, csv.Value);
    }

    static async Task<(Project Project, Contract Contract)> SeedProjectAsync(HarnessBag h, RevenueMethod method, decimal contractAmount)
    {
        var client = Client.Create("客", ClientKind.External, null, ClientLifecycle.Active);
        await h.Clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), contractAmount, "TWD", PricingKind.FixedPrice, []);
        await h.Contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), method, DateTimeOffset.UtcNow);
        await h.Projects.AddAsync(project);
        return (project, contract);
    }

    static HarnessBag Harness()
    {
        var user = new StubUser { Role = PlatformRole.Owner, PersonId = Guid.NewGuid(), DisplayName = "管理員", UserName = "owner" };
        var clock = new MemoryClock();
        var audit = new AuditWriter(new MemoryAudit(), user, clock);
        var people = new MemoryPeople();
        var timesheets = new MemoryTimesheets();
        var assignments = new MemoryAssignments();
        var projects = new MemoryProjects();
        var clients = new MemoryClients();
        var contracts = new MemoryContracts();
        var periods = new MemoryPayroll();
        var settings = new MemorySettings();
        var encryptor = new PassEncryptor();
        var gate = new AuthorizationGate(user);
        var monthly = new MonthlySalaryCalculator();
        var hourly = new HourlyCalculator();
        var bonus = new ProjectBonusCalculator();
        var payroll = new PayrollCommands(
            periods, people, timesheets, assignments, projects, gate, encryptor,
            monthly, hourly, bonus, new MemoryUow(), audit, clock);
        var timesheetsCmd = new TimesheetCommands(
            timesheets, people, projects, new MemoryUnmatched(), new EmptyInvites(),
            gate, new TimesheetIdempotency(), new MemoryUow(), audit, clock, user);
        IRevenueRecognizer[] recognizers =
        [
            new MilestoneRevenueRecognizer(),
            new StraightLineRevenueRecognizer(),
            new TimeAndMaterialsRevenueRecognizer(),
        ];
        var budget = new BudgetCommands(
            projects, contracts, clients, timesheets, periods, people, assignments, settings,
            gate, encryptor, recognizers, monthly, hourly, new MemoryUow(), clock, user);
        return new HarnessBag(budget, payroll, timesheetsCmd, people, timesheets, assignments, projects, clients, contracts, periods, encryptor);
    }

    sealed record HarnessBag(
        BudgetCommands Budget,
        PayrollCommands Payroll,
        TimesheetCommands TimesheetsCmd,
        MemoryPeople People,
        MemoryTimesheets Timesheets,
        MemoryAssignments Assignments,
        MemoryProjects Projects,
        MemoryClients Clients,
        MemoryContracts Contracts,
        MemoryPayroll Periods,
        PassEncryptor Encryptor);
}
