using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

sealed class MemoryAssignments : IAssignmentRepository
{
    public List<Assignment> Items { get; } = [];

    public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(a => a.Id == id));

    public Task<IReadOnlyList<Assignment>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Assignment>>(Items);

    public Task<IReadOnlyList<Assignment>> ListForWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        var end = weekStart.AddDays(6);
        return Task.FromResult<IReadOnlyList<Assignment>>(Items.Where(a => a.Overlaps(weekStart, end)).ToList());
    }

    public Task<IReadOnlyList<Assignment>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Assignment>>(Items.Where(a => a.PersonId == personId).ToList());

    public Task<IReadOnlyList<Assignment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Assignment>>(Items.Where(a => a.ProjectId == projectId).ToList());

    public Task AddAsync(Assignment assignment, CancellationToken ct = default)
    {
        Items.Add(assignment);
        return Task.CompletedTask;
    }
}

sealed class MemorySettings : ISettingsRepository
{
    public CompanySettings Settings { get; } = CompanySettings.CreateDefault();
    public Task<CompanySettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);
}

sealed class TrackingGitHub : IGitHubDirectory
{
    public List<(string Repo, int Number, string Login)> Assignees { get; } = [];
    public bool FailWriteBack { get; set; }
    public IReadOnlyList<GitHubIssueRef> OpenIssues { get; set; } = [];

    public Task<IReadOnlyList<GitHubMember>> ListOrgMembersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitHubMember>>([]);

    public Task<IReadOnlyList<string>> ListReposAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(["acme/app", "acme/billing"]);

    public Task<IReadOnlyList<GitHubIssueRef>> ListOpenIssuesAsync(string ownerRepo, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitHubIssueRef>>(OpenIssues.Where(i =>
            string.Equals(i.Repo, ownerRepo, StringComparison.OrdinalIgnoreCase)).ToList());

    public Task<Outcome> AddAssigneeAsync(string ownerRepo, int issueNumber, string login, CancellationToken cancellationToken = default)
    {
        if (FailWriteBack)
            return Task.FromResult(Outcome.Fail("github_writeback", "寫回失敗"));
        Assignees.Add((ownerRepo, issueNumber, login));
        return Task.FromResult(Outcome.Success());
    }

    public Task<string?> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

public class DispatchCommandsTests
{
    [Fact]
    public async Task Vendor_not_authorized_is_rejected()
    {
        var h = Harness();
        var vendor = Vendor.Create("外包甲", null, new DateOnly(2026, 1, 1), null);
        await h.Vendors.AddAsync(vendor);
        var person = Person.Create("派駐", EmploymentKind.VendorStaff, vendor.Id, DateTimeOffset.UtcNow);
        person.BindGitHub("bob", _ => null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedActiveProjectAsync(h, authorizedVendors: []);

        var result = await h.Commands.AssignAsync(person.Id, project.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 8, AssignmentRole.Engineer, Array.Empty<(string, int)>(), null, null);
        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.VendorNotAuthorized, result.Code);
    }

    [Fact]
    public async Task Force_overload_writes_audit_and_cancel_keeps_timesheet()
    {
        var h = Harness(PlatformRole.Delivery);
        var person = Person.Create("Alice", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.BindGitHub("alice", _ => null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedActiveProjectAsync(h);
        var first = await h.Commands.AssignAsync(person.Id, project.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 40, AssignmentRole.Engineer, Array.Empty<(string, int)>(), null, null);
        Assert.True(first.Ok, first.Message);

        var forced = await h.Commands.AssignAsync(person.Id, project.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 8, AssignmentRole.Engineer, Array.Empty<(string, int)>(), null, "客戶現場");
        Assert.True(forced.Ok, forced.Message);
        Assert.Contains(h.Audit.Entries, e => e.Action == AuditActions.ForceOverload);

        var sheet = Timesheet.Upload("slot-1", person.Id, project.Id, new DateOnly(2026, 9, 2), 4, [], [], false, null, DateTimeOffset.UtcNow);
        await h.Timesheets.AddAsync(sheet);
        var cancel = await h.Commands.CancelAsync(forced.Value);
        Assert.True(cancel.Ok, cancel.Message);
        Assert.Equal(AssignmentStatus.Cancelled, h.Assignments.Items.Single(a => a.Id == forced.Value).Status);
        Assert.Single(h.Timesheets.Items);
        Assert.Equal(TimesheetStatus.PendingPm, h.Timesheets.Items[0].Status);
    }

    [Fact]
    public async Task Writeback_uses_issue_repo_and_marks_synced()
    {
        var h = Harness();
        h.GitHub.OpenIssues =
        [
            new("acme/billing", 42, "修帳", null, DateTimeOffset.UtcNow, "https://x"),
        ];
        var person = Person.Create("Alice", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.BindGitHub("alice", _ => null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedActiveProjectAsync(h);
        project.AddRepo("acme/app");
        project.AddRepo("acme/billing");

        var result = await h.Commands.AssignAsync(
            person.Id,
            project.Id,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 5),
            8,
            AssignmentRole.Engineer,
            [("acme/billing", 42)],
            null,
            null);
        Assert.True(result.Ok, result.Message);
        var assignment = h.Assignments.Items.Single();
        Assert.Equal(AssignmentSource.FromIssue, assignment.Source);
        Assert.Equal(AssignmentSyncState.Synced, assignment.SyncState);
        Assert.Contains(h.GitHub.Assignees, a => a.Repo == "acme/billing" && a.Number == 42 && a.Login == "alice");
    }

    [Fact]
    public async Task Writeback_failure_marks_pending_with_note()
    {
        var h = Harness();
        h.GitHub.FailWriteBack = true;
        var person = Person.Create("Alice", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.BindGitHub("alice", _ => null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedActiveProjectAsync(h);
        project.AddRepo("acme/app");

        var result = await h.Commands.AssignAsync(
            person.Id,
            project.Id,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 5),
            8,
            AssignmentRole.Engineer,
            [("acme/app", 7)],
            null,
            null);
        Assert.True(result.Ok, result.Message);
        var assignment = h.Assignments.Items.Single();
        Assert.Equal(AssignmentSyncState.Pending, assignment.SyncState);
        Assert.Contains("寫回失敗", assignment.SyncNote);
    }

    [Fact]
    public async Task No_issue_writeback_is_not_required()
    {
        var h = Harness();
        var person = Person.Create("Alice", EmploymentKind.FullTime, null, DateTimeOffset.UtcNow);
        person.BindGitHub("alice", _ => null);
        await h.People.AddAsync(person);
        var (project, _) = await SeedActiveProjectAsync(h);
        project.AddRepo("acme/app");

        var result = await h.Commands.AssignAsync(person.Id, project.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), 8, AssignmentRole.Engineer, Array.Empty<(string, int)>(), null, null);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(AssignmentSyncState.NotRequired, h.Assignments.Items.Single().SyncState);
    }

    static async Task<(Project Project, Contract Contract)> SeedActiveProjectAsync(HarnessBag h, IEnumerable<Guid>? authorizedVendors = null)
    {
        var client = Client.Create("客", ClientKind.External, null, ClientLifecycle.Active);
        await h.Clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, authorizedVendors ?? []);
        await h.Contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, DateTimeOffset.UtcNow);
        await h.Projects.AddAsync(project);
        return (project, contract);
    }

    static HarnessBag Harness(PlatformRole role = PlatformRole.Delivery)
    {
        var user = new StubUser { Role = role, PersonId = Guid.NewGuid(), DisplayName = "交付", UserName = "delivery" };
        var clock = new MemoryClock();
        var auditLog = new MemoryAudit();
        var audit = new AuditWriter(auditLog, user, clock);
        var people = new MemoryPeople();
        var vendors = new MemoryVendors();
        var clients = new MemoryClients();
        var contracts = new MemoryContracts();
        var projects = new MemoryProjects();
        var assignments = new MemoryAssignments();
        var timesheets = new MemoryTimesheets();
        var settings = new MemorySettings();
        var github = new TrackingGitHub();
        var availability = new AvailabilityCalculator();
        var commands = new DispatchCommands(
            people,
            projects,
            contracts,
            clients,
            assignments,
            settings,
            github,
            new AuthorizationGate(user),
            availability,
            new ContractStaffingPolicy(),
            new AssignmentSuggester(availability),
            new MemoryUow(),
            audit,
            clock,
            user);
        return new HarnessBag(commands, people, vendors, clients, contracts, projects, assignments, timesheets, auditLog, github);
    }

    sealed record HarnessBag(
        DispatchCommands Commands,
        MemoryPeople People,
        MemoryVendors Vendors,
        MemoryClients Clients,
        MemoryContracts Contracts,
        MemoryProjects Projects,
        MemoryAssignments Assignments,
        MemoryTimesheets Timesheets,
        MemoryAudit Audit,
        TrackingGitHub GitHub);
}

sealed class MemoryVendors : IVendorRepository
{
    public List<Vendor> Items { get; } = [];
    public Task<Vendor?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(v => v.Id == id));
    public Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Vendor>>(Items);
    public Task AddAsync(Vendor vendor, CancellationToken ct = default)
    {
        Items.Add(vendor);
        return Task.CompletedTask;
    }
}
