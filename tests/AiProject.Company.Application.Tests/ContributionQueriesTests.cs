using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

sealed class MemoryTimesheets : ITimesheetRepository
{
    public List<Timesheet> Items { get; } = [];

    public Task<Timesheet?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t => t.Id == id));

    public Task<Timesheet?> GetByLocalSlotAsync(string localSlotId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(t =>
            string.Equals(t.LocalSlotId, localSlotId, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Timesheet>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Timesheet>>(Items);

    public Task<IReadOnlyList<Timesheet>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Timesheet>>(Items.Where(t => t.PersonId == personId).ToList());

    public Task<IReadOnlyList<Timesheet>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Timesheet>>(Items.Where(t => t.ProjectId == projectId).ToList());

    public Task<IReadOnlyList<Timesheet>> ListForPeriodAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Timesheet>>(Items.Where(t => t.WorkDate >= start && t.WorkDate <= end).ToList());

    public Task AddAsync(Timesheet timesheet, CancellationToken ct = default)
    {
        Items.Add(timesheet);
        return Task.CompletedTask;
    }
}

sealed class MemoryPeople : IPersonRepository
{
    public List<Person> Items { get; } = [];

    public Task<Person?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<Person?> GetByGitHubAsync(string login, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p =>
            string.Equals(p.GitHubLogin, login, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Person>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Person>>(Items);

    public Task AddAsync(Person person, CancellationToken ct = default)
    {
        Items.Add(person);
        return Task.CompletedTask;
    }
}

public class ContributionQueriesTests
{
    [Fact]
    public async Task Aggregates_hours_issues_and_types_per_person()
    {
        var now = DateTimeOffset.UtcNow;
        var alice = Person.Create("Alice", EmploymentKind.FullTime, null, now);
        Assert.True(alice.BindGitHub("alice", _ => null).Ok);
        var bob = Person.Create("Bob", EmploymentKind.FullTime, null, now);

        var projects = new MemoryProjects();
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        var project = Project.Create(contract.Id, "Member", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, now);
        var projectId = project.Id;
        projects.Items.Add(project);

        var people = new MemoryPeople { Items = { alice, bob } };
        var sheets = new MemoryTimesheets();
        sheets.Items.Add(Timesheet.Upload("a1", alice.Id, projectId, new DateOnly(2026, 9, 1), 8, [10, 11], [], false, null, now, ["code", "issue"]));
        sheets.Items.Add(Timesheet.Upload("a2", alice.Id, projectId, new DateOnly(2026, 9, 2), 4, [11], [], false, null, now, ["docs"]));
        sheets.Items.Add(Timesheet.Upload("b1", bob.Id, projectId, new DateOnly(2026, 9, 1), 6, [20], [], false, null, now, ["code"]));
        var returned = Timesheet.Upload("r1", bob.Id, projectId, new DateOnly(2026, 9, 3), 2, [99], [], false, null, now, ["code"]);
        returned.ReturnToEngineer("重填");
        sheets.Items.Add(returned);

        var user = new StubUser
        {
            Role = PlatformRole.Pm,
            PersonId = Guid.NewGuid(),
            AuthorizedProjectIds = new HashSet<Guid> { projectId },
        };
        var q = new ContributionQueries(sheets, people, projects, new AuthorizationGate(user), user);
        var view = await q.ForProjectAsync(projectId);

        Assert.NotNull(view);
        Assert.Null(view.EmptyHint);
        Assert.Equal(18, view.TotalHours);
        Assert.Equal(2, view.ContributorCount);
        Assert.Equal(2, view.Contributors.Count);
        var aliceRow = view.Contributors.Single(c => c.PersonId == alice.Id);
        Assert.Equal(12, aliceRow.Hours);
        Assert.Equal([10, 11], aliceRow.IssueNumbers.ToList());
        Assert.Equal("github:alice", aliceRow.GitHubKey);
        Assert.Contains(aliceRow.Types, t => t.Type == "code" && t.Count == 1);
        Assert.Contains(view.TypeTotals, t => t.Type == "code" && t.Count == 2);
    }

    [Fact]
    public async Task Engineer_only_sees_own_rows()
    {
        var now = DateTimeOffset.UtcNow;
        var alice = Person.Create("Alice", EmploymentKind.FullTime, null, now);
        var bob = Person.Create("Bob", EmploymentKind.FullTime, null, now);
        var client = Client.Create("客", ClientKind.External, null);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, now);
        var projects = new MemoryProjects { Items = { project } };
        var people = new MemoryPeople { Items = { alice, bob } };
        var sheets = new MemoryTimesheets();
        sheets.Items.Add(Timesheet.Upload("a1", alice.Id, project.Id, new DateOnly(2026, 9, 1), 8, [], [], false, null, now));
        sheets.Items.Add(Timesheet.Upload("b1", bob.Id, project.Id, new DateOnly(2026, 9, 1), 8, [], [], false, null, now));

        var user = new StubUser { Role = PlatformRole.Engineer, PersonId = alice.Id };
        var q = new ContributionQueries(sheets, people, projects, new AuthorizationGate(user), user);
        var view = await q.ForProjectAsync(project.Id);

        Assert.NotNull(view);
        Assert.Single(view.Contributors);
        Assert.Equal(alice.Id, view.Contributors[0].PersonId);
        Assert.Equal(8, view.TotalHours);
    }
}
