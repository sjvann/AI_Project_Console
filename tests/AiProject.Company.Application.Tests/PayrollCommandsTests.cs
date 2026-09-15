using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

sealed class MemoryPayroll : IPayrollRepository
{
    public List<PayrollPeriod> Items { get; } = [];

    public Task<PayrollPeriod?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<PayrollPeriod?> GetByRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Start == start && p.End == end));

    public Task<IReadOnlyList<PayrollPeriod>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PayrollPeriod>>(Items);

    public Task AddAsync(PayrollPeriod period, CancellationToken ct = default)
    {
        Items.Add(period);
        return Task.CompletedTask;
    }
}

public class PayrollCommandsTests
{
    [Fact]
    public async Task Confirm_open_lock_export_csv_kpi01()
    {
        var h = Harness();
        var person = Person.Create("王工程", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers("80000", null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedProjectAsync(h);
        var sheet = Timesheet.Upload("slot-pay", person.Id, project.Id, new DateOnly(2026, 9, 3), 8, [], [], false, null, DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);

        var confirm = await h.TimesheetsCmd.ConfirmAsync(sheet.Id);
        Assert.True(confirm.Ok, confirm.Message);
        Assert.Equal(TimesheetStatus.Approved, sheet.Status);

        var opened = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.True(opened.Ok, opened.Message);
        var period = h.Periods.Items.Single();
        Assert.Contains(period.Lines, l => l.Kind == PayrollLineKind.Monthly && l.Payable && l.Amount == 80000);

        var lockResult = await h.Payroll.LockAsync(period.Id, "九月結算");
        Assert.True(lockResult.Ok, lockResult.Message);
        Assert.Equal(PayrollPeriodStatus.Locked, period.Status);

        var csv = await h.Payroll.ExportCsvAsync(period.Id);
        Assert.True(csv.Ok, csv.Message);
        Assert.StartsWith(PayrollCsv.Header, csv.Value);
        Assert.Contains("Monthly", csv.Value);
        Assert.Contains("80000", csv.Value);
    }

    [Fact]
    public async Task Bonus_payable_milestone_enters_payroll()
    {
        var h = Harness();
        var person = Person.Create("外包", EmploymentKind.Freelance, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers(null, "1000");
        await h.People.AddAsync(person);
        var (project, _) = await SeedProjectAsync(h);
        var ms = project.AddMilestone("交付", new DateOnly(2026, 9, 15), 12000, null);
        var assign = Assignment.Create(
            person,
            project,
            h.Contracts.Items.Single(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            20,
            AssignmentRole.Engineer,
            AssignmentSource.Manual,
            new ContractStaffingPolicy(),
            new PersonAvailability(person.Id, 40, 0, 40, false),
            null,
            false,
            false,
            DateTimeOffset.UtcNow);
        Assert.True(assign.Ok, assign.Message);
        await h.Assignments.AddAsync(assign.Value!);

        var openedBefore = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.True(openedBefore.Ok, openedBefore.Message);
        Assert.DoesNotContain(h.Periods.Items[0].Lines, l => l.Kind == PayrollLineKind.ProjectBonus && l.Payable && l.Amount > 0);

        var mark = await h.ProjectsCmd.SetMilestoneBonusPayableAsync(project.Id, ms.Id, true);
        Assert.True(mark.Ok, mark.Message);
        var period = PayrollPeriod.Open(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        await h.Periods.AddAsync(period);
        var recalc = await h.Payroll.RecalculateAsync(period.Id);
        Assert.True(recalc.Ok, recalc.Message);
        Assert.Contains(period.Lines, l => l.Kind == PayrollLineKind.ProjectBonus && l.Payable && l.Amount == 12000);
    }

    [Fact]
    public async Task Correction_requires_locked_period()
    {
        var h = Harness();
        var opened = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null);
        Assert.True(opened.Ok, opened.Message);
        var bad = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), opened.Value);
        Assert.False(bad.Ok);
        Assert.Equal(ErrorCodes.InvalidState, bad.Code);

        var lockResult = await h.Payroll.LockAsync(opened.Value, "結算");
        Assert.True(lockResult.Ok, lockResult.Message);
        var ok = await h.Payroll.OpenAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), opened.Value);
        Assert.True(ok.Ok, ok.Message);
        Assert.Equal(opened.Value, h.Periods.Items.Single(p => p.Id == ok.Value).CorrectsPeriodId);
    }

    static async Task<(Project Project, Contract Contract)> SeedProjectAsync(HarnessBag h)
    {
        var client = Client.Create("客", ClientKind.External, null, ClientLifecycle.Active);
        await h.Clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        await h.Contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
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
        var encryptor = new PassEncryptor();
        var gate = new AuthorizationGate(user);
        var payroll = new PayrollCommands(
            periods,
            people,
            timesheets,
            assignments,
            projects,
            gate,
            encryptor,
            new MonthlySalaryCalculator(),
            new HourlyCalculator(),
            new ProjectBonusCalculator(),
            new MemoryUow(),
            audit,
            clock);
        var timesheetsCmd = new TimesheetCommands(
            timesheets,
            people,
            projects,
            new MemoryUnmatched(),
            new EmptyInvites(),
            gate,
            new TimesheetIdempotency(),
            new MemoryUow(),
            audit,
            clock,
            user);
        var projectsCmd = new ProjectCommands(
            clients,
            contracts,
            projects,
            assignments,
            new EmptyGithub(),
            new EmptyIntake(),
            new EmptyDocs(),
            gate,
            encryptor,
            new MemoryUow(),
            clock,
            user,
            audit);
        return new HarnessBag(payroll, timesheetsCmd, projectsCmd, people, timesheets, assignments, projects, clients, contracts, periods);
    }

    sealed record HarnessBag(
        PayrollCommands Payroll,
        TimesheetCommands TimesheetsCmd,
        ProjectCommands ProjectsCmd,
        MemoryPeople People,
        MemoryTimesheets Timesheets,
        MemoryAssignments Assignments,
        MemoryProjects Projects,
        MemoryClients Clients,
        MemoryContracts Contracts,
        MemoryPayroll Periods);
}

sealed class MemoryUnmatched : IUnmatchedUploadRepository
{
    public Task<IReadOnlyList<UnmatchedUpload>> ListOpenAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<UnmatchedUpload>>([]);
    public Task AddAsync(UnmatchedUpload item, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class EmptyInvites : IInvitationRepository
{
    public Task<Invitation?> GetByGitHubAsync(string login, CancellationToken ct = default) => Task.FromResult<Invitation?>(null);
    public Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Invitation>>([]);
    public Task AddAsync(Invitation invitation, CancellationToken ct = default) => Task.CompletedTask;
}
