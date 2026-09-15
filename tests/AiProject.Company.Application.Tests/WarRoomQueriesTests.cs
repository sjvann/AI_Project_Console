using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

public class WarRoomQueriesTests
{
    [Fact]
    public async Task Engineer_denied_server_side_kpi06()
    {
        var h = Harness(PlatformRole.Engineer);
        await Assert.ThrowsAsync<DomainException>(() => h.WarRoom.LoadAsync());
    }

    [Fact]
    public async Task Vendor_denied_server_side_kpi03()
    {
        var h = Harness(PlatformRole.VendorAdmin, vendorId: Guid.NewGuid());
        var ex = await Assert.ThrowsAsync<DomainException>(() => h.WarRoom.LoadAsync());
        Assert.Equal(ErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task Exec_sees_overdue_red_and_can_read_margin_kpi02()
    {
        var h = Harness(PlatformRole.Exec);
        var (project, client) = await SeedActiveProjectAsync(h, targetEnd: new DateOnly(2026, 9, 5));
        project.AddMilestone("交付", new DateOnly(2026, 9, 1), 100000, null);
        var ms = project.Milestones[0];
        ms.SetRecognition(RecognitionStep.Collected);

        var person = Person.Create("王", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.SetCompensationCiphers("80000", null);
        await h.People.AddAsync(person);
        var sheet = Timesheet.Upload("wr1", person.Id, project.Id, new DateOnly(2026, 9, 3), 40, [], [], false, null, DateTimeOffset.UtcNow);
        sheet.Confirm(DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);

        var snap = await h.WarRoom.LoadAsync();
        Assert.Equal(1, snap.ActiveProjects);
        Assert.True(snap.RedCount >= 1);
        var card = Assert.Single(snap.Cards);
        Assert.Equal(HealthTone.Red, card.Health.Schedule);
        Assert.Equal(client.Name, card.ClientName);
        Assert.Contains(snap.Exceptions, e => e.Kind == "overdue");
        // exec 無 ManageBudget，仍應能讀到本月毛利
        Assert.NotNull(card.MarginPercent);
        Assert.Equal(2026, snap.MarginYear);
        Assert.Equal(9, snap.MarginMonth);
    }

    [Fact]
    public async Task Exclude_from_margin_kpi_not_in_month_average()
    {
        var h = Harness(PlatformRole.Delivery);
        var (project, _) = await SeedActiveProjectAsync(h, targetEnd: new DateOnly(2026, 12, 31));
        project.Update(project.Name, project.Start, project.TargetEnd, project.RevenueMethod, null, excludeFromMargin: true, null);
        var snap = await h.WarRoom.LoadAsync();
        var card = Assert.Single(snap.Cards);
        Assert.Null(card.MarginPercent);
        Assert.Equal("不列入毛利", card.Health.MarginLabel);
        Assert.Null(snap.MonthMargin);
    }

    [Fact]
    public async Task Missing_three_workdays_raises_hours_exception()
    {
        var h = Harness(PlatformRole.Delivery);
        // Clock is 2026-09-06 Saturday → lookback workdays Fri/Thu/Wed = 9/5, 9/4, 9/3
        var (project, _) = await SeedActiveProjectAsync(h, targetEnd: new DateOnly(2026, 12, 31));
        var person = Person.Create("缺工", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        await h.People.AddAsync(person);
        var assign = Assignment.Create(
            person, project, h.Contracts.Items.Single(),
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 11), 20,
            AssignmentRole.Engineer, AssignmentSource.Manual,
            new ContractStaffingPolicy(),
            new PersonAvailability(person.Id, 40, 0, 40, false),
            null, false, false, DateTimeOffset.UtcNow);
        Assert.True(assign.Ok, assign.Message);
        await h.Assignments.AddAsync(assign.Value!);

        var snap = await h.WarRoom.LoadAsync();
        Assert.Contains(snap.Exceptions, e => e.Kind == "hours" && e.Text.Contains("連續 3 個工作日", StringComparison.Ordinal));
    }

    static async Task<(Project Project, Client Client)> SeedActiveProjectAsync(HarnessBag h, DateOnly targetEnd)
    {
        var client = Client.Create("晨星", ClientKind.External, null, ClientLifecycle.Active);
        await h.Clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 200000, "TWD", PricingKind.FixedPrice, []);
        await h.Contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "逾期專案", new DateOnly(2026, 1, 1), targetEnd, RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        await h.Projects.AddAsync(project);
        return (project, client);
    }

    static HarnessBag Harness(PlatformRole role, Guid? vendorId = null)
    {
        var user = new StubUser
        {
            Role = role,
            PersonId = Guid.NewGuid(),
            VendorId = vendorId,
            DisplayName = role.ToString(),
            UserName = role.ToString().ToLowerInvariant(),
        };
        var clock = new MemoryClock();
        var people = new MemoryPeople();
        var timesheets = new MemoryTimesheets();
        var assignments = new MemoryAssignments();
        var projects = new MemoryProjects();
        var clients = new MemoryClients();
        var contracts = new MemoryContracts();
        var periods = new MemoryPayroll();
        var settings = new MemorySettings();
        settings.Settings.Update("測試公司", "Asia/Taipei", "TWD", 25, 10, true);
        var encryptor = new PassEncryptor();
        var gate = new AuthorizationGate(user);
        var monthly = new MonthlySalaryCalculator();
        var hourly = new HourlyCalculator();
        IRevenueRecognizer[] recognizers =
        [
            new MilestoneRevenueRecognizer(),
            new StraightLineRevenueRecognizer(),
            new TimeAndMaterialsRevenueRecognizer(),
        ];
        var budget = new BudgetCommands(
            projects, contracts, clients, timesheets, periods, people, assignments, settings,
            gate, encryptor, recognizers, monthly, hourly, new MemoryUow(), clock, user);
        var warRoom = new WarRoomQueries(
            projects, assignments, timesheets, people, clients, contracts,
            new EmptyGithub(), gate, new ProjectHealthPolicyAdapter(settings),
            new AvailabilityCalculator(), clock, settings, budget, new EmptyDocs());
        return new HarnessBag(warRoom, budget, people, timesheets, assignments, projects, clients, contracts, periods);
    }

    sealed record HarnessBag(
        WarRoomQueries WarRoom,
        BudgetCommands Budget,
        MemoryPeople People,
        MemoryTimesheets Timesheets,
        MemoryAssignments Assignments,
        MemoryProjects Projects,
        MemoryClients Clients,
        MemoryContracts Contracts,
        MemoryPayroll Periods);
}
