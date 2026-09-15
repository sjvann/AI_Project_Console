using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

sealed class MemoryClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 6, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today(string timeZoneId) => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

sealed class MemoryUow : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}

sealed class MemoryAudit : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];
    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<AuditEntry>> QueryAsync(string? entityType, Guid? entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Entries);
}

sealed class PassEncryptor : IFieldEncryptor
{
    public string Encrypt(string plaintext) => plaintext;
    public string Decrypt(string ciphertext) => ciphertext;
}

sealed class MemoryClients : IClientRepository
{
    public List<Client> Items { get; } = [];
    public Task<Client?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));
    public Task<IReadOnlyList<Client>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Client>>(Items);
    public Task AddAsync(Client client, CancellationToken ct = default)
    {
        Items.Add(client);
        return Task.CompletedTask;
    }
}

sealed class MemoryContracts : IContractRepository
{
    public List<Contract> Items { get; } = [];
    public Task<Contract?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(c => c.Id == id));
    public Task<IReadOnlyList<Contract>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Contract>>(Items.Where(c => !c.IsDeleted).ToList());
    public Task<IReadOnlyList<Contract>> ListByClientAsync(Guid clientId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Contract>>(Items.Where(c => c.ClientId == clientId && !c.IsDeleted).ToList());
    public Task AddAsync(Contract contract, CancellationToken ct = default)
    {
        Items.Add(contract);
        return Task.CompletedTask;
    }
}

sealed class MemoryProjects : IProjectRepository
{
    public List<Project> Items { get; } = [];
    public Task<Project?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(p => p.Id == id));
    public Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => string.Equals(p.ProjectCode, projectCode.Trim(), StringComparison.OrdinalIgnoreCase)));
    public Task<Project?> FindByRepoAsync(string ownerRepo, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Repos.Any(r => string.Equals(r.OwnerRepo, ownerRepo.Trim(), StringComparison.OrdinalIgnoreCase))));
    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Project>>(Items);
    public Task AddAsync(Project project, CancellationToken ct = default)
    {
        Items.Add(project);
        return Task.CompletedTask;
    }
}

sealed class EmptyAssignments : IAssignmentRepository
{
    public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Assignment?>(null);
    public Task<IReadOnlyList<Assignment>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Assignment>>([]);
    public Task<IReadOnlyList<Assignment>> ListForWeekAsync(DateOnly weekStart, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Assignment>>([]);
    public Task<IReadOnlyList<Assignment>> ListForPersonAsync(Guid personId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Assignment>>([]);
    public Task<IReadOnlyList<Assignment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Assignment>>([]);
    public Task AddAsync(Assignment assignment, CancellationToken ct = default) => Task.CompletedTask;
}

sealed class EmptyGithub : IGitHubDirectory
{
    public Task<IReadOnlyList<GitHubMember>> ListOrgMembersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubMember>>([]);
    public Task<IReadOnlyList<string>> ListReposAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(["acme/app"]);
    public Task<IReadOnlyList<GitHubIssueRef>> ListOpenIssuesAsync(string ownerRepo, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubIssueRef>>([]);
    public Task<Outcome> AddAssigneeAsync(string ownerRepo, int issueNumber, string login, CancellationToken cancellationToken = default) => Task.FromResult(Outcome.Success());
    public Task<string?> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

sealed class EmptyIntake : IWorkspaceIntakeReader
{
    public Task<IntakeSummary?> ReadAsync(string workspacePath, CancellationToken ct = default) => Task.FromResult<IntakeSummary?>(null);
}

sealed class EmptyDocs : IProjectDocsCatalog
{
    public Task<ProjectDocsCatalog> ReadAsync(Project project, CancellationToken ct = default) =>
        Task.FromResult(ProjectDocsCatalog.Empty("先掛倉或連工作區，需求分析在控制台寫、這裡只當入口。"));
}

public class EstablishCommandTests
{
    [Fact]
    public async Task Lead_cannot_establish()
    {
        var (commands, clients, _, _) = Harness();
        var lead = Client.Create("潛在", ClientKind.External, "窗口", ClientLifecycle.Lead);
        await clients.AddAsync(lead);
        var result = await commands.EstablishAsync(Request(lead.Id));
        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.ClientCannotEstablish, result.Code);
    }

    [Fact]
    public async Task Proposal_establish_creates_contract_project_activity_and_marks_active()
    {
        var (commands, clients, contracts, projects) = Harness();
        var client = Client.Create("議約", ClientKind.External, "窗口", ClientLifecycle.Proposal);
        await clients.AddAsync(client);
        var result = await commands.EstablishAsync(Request(client.Id, "雲端專案", "acme/app"));
        Assert.True(result.Ok, result.Message);
        Assert.Equal(ClientLifecycle.Active, client.Lifecycle);
        Assert.Contains(client.Activities, a => a.Kind == ClientActivityKind.ProjectEstablished && a.Summary.Contains("雲端專案"));
        Assert.Single(contracts.Items);
        var project = Assert.Single(projects.Items);
        Assert.Equal("雲端專案", project.Name);
        Assert.Equal(ProjectStatus.Active, project.Status);
        Assert.Contains(project.Journals, j => j.Kind == ProjectJournalKind.Established);
        Assert.Contains(project.Repos, r => r.OwnerRepo == "acme/app");
        Assert.Equal("需求", project.CurrentPhase(DateTimeOffset.UtcNow)?.Name);
    }

    [Fact]
    public async Task Set_project_code_and_pause_status()
    {
        var user = new StubUser { Role = PlatformRole.Delivery, PersonId = Guid.NewGuid(), DisplayName = "交付", UserName = "delivery" };
        var (commands, clients, _, projects) = Harness(user);
        var client = Client.Create("議約", ClientKind.External, "窗口", ClientLifecycle.Proposal);
        await clients.AddAsync(client);
        var created = await commands.EstablishAsync(Request(client.Id, "碼專案"));
        Assert.True(created.Ok, created.Message);
        var project = Assert.Single(projects.Items);

        var code = await commands.SetProjectCodeAsync(project.Id, "ACME-99");
        Assert.True(code.Ok, code.Message);
        Assert.Equal("ACME-99", project.ProjectCode);

        var paused = await commands.SetProjectStatusAsync(project.Id, ProjectStatus.Paused);
        Assert.True(paused.Ok, paused.Message);
        Assert.Equal(ProjectStatus.Paused, project.Status);
    }

    static (ProjectCommands Commands, MemoryClients Clients, MemoryContracts Contracts, MemoryProjects Projects) Harness(StubUser? user = null)
    {
        user ??= new StubUser { Role = PlatformRole.Pm, PersonId = Guid.NewGuid(), DisplayName = "周專案", UserName = "pm" };
        var clock = new MemoryClock();
        var audit = new AuditWriter(new MemoryAudit(), user, clock);
        var clients = new MemoryClients();
        var contracts = new MemoryContracts();
        var projects = new MemoryProjects();
        var commands = new ProjectCommands(
            clients, contracts, projects, new EmptyAssignments(), new EmptyGithub(), new EmptyIntake(), new EmptyDocs(),
            new AuthorizationGate(user), new PassEncryptor(), new MemoryUow(), clock, user, audit);
        return (commands, clients, contracts, projects);
    }

    static EstablishProjectRequest Request(Guid clientId, string project = "專案", string? repo = null) =>
        new(clientId, null, "合約", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), 1000, "TWD", PricingKind.FixedPrice, [], project, new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, true, repo, null);
}
