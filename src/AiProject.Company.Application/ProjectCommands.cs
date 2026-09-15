using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class ProjectCommands
{
    readonly IClientRepository _clients;
    readonly IContractRepository _contracts;
    readonly IProjectRepository _projects;
    readonly IAssignmentRepository _assignments;
    readonly IGitHubDirectory _github;
    readonly IWorkspaceIntakeReader _intake;
    readonly IProjectDocsCatalog _docs;
    readonly IAuthorizationGate _auth;
    readonly IFieldEncryptor _encryptor;
    readonly IUnitOfWork _uow;
    readonly IClock _clock;
    readonly ICurrentUser _user;
    readonly AuditWriter _audit;

    public ProjectCommands(
        IClientRepository clients,
        IContractRepository contracts,
        IProjectRepository projects,
        IAssignmentRepository assignments,
        IGitHubDirectory github,
        IWorkspaceIntakeReader intake,
        IProjectDocsCatalog docs,
        IAuthorizationGate auth,
        IFieldEncryptor encryptor,
        IUnitOfWork uow,
        IClock clock,
        ICurrentUser user,
        AuditWriter audit)
    {
        _clients = clients;
        _contracts = contracts;
        _projects = projects;
        _assignments = assignments;
        _github = github;
        _intake = intake;
        _docs = docs;
        _auth = auth;
        _encryptor = encryptor;
        _uow = uow;
        _clock = clock;
        _user = user;
        _audit = audit;
    }

    public async Task<Outcome<Guid>> CreateClientAsync(
        string name,
        ClientKind kind,
        string? contact,
        ClientLifecycle lifecycle = ClientLifecycle.Lead,
        ClientSource source = ClientSource.SelfDeveloped,
        string? phone = null,
        string? email = null,
        DateOnly? nextFollowUp = null,
        Guid? ownerPersonId = null,
        CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var client = Client.Create(name, kind, contact, lifecycle, source);
        client.UpdateProfile(client.Name, client.Kind, client.Contact, phone, email, source, nextFollowUp, ownerPersonId);
        await _clients.AddAsync(client, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(client.Id);
    }

    public async Task<Outcome> UpdateClientAsync(Guid id, string name, ClientKind kind, string? contact, CancellationToken ct = default)
    {
        var client = await _clients.GetAsync(id, ct);
        if (client is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return gate;
        client.Update(name, kind, contact);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> UpdateClientProfileAsync(
        Guid id,
        string name,
        ClientKind kind,
        string? contact,
        string? phone,
        string? email,
        ClientSource source,
        DateOnly? nextFollowUp,
        Guid? ownerPersonId,
        CancellationToken ct = default)
    {
        var client = await _clients.GetAsync(id, ct);
        if (client is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return gate;
        client.UpdateProfile(name, kind, contact, phone, email, source, nextFollowUp, ownerPersonId);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> ChangeClientLifecycleAsync(Guid id, ClientLifecycle to, DateOnly? nextFollowUp, CancellationToken ct = default)
    {
        var client = await _clients.GetAsync(id, ct);
        if (client is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return gate;
        var changed = client.ChangeLifecycle(to, _clock.UtcNow, Actor(), nextFollowUp);
        if (!changed.Ok)
            return changed;
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> AddClientActivityAsync(Guid id, ClientActivityKind kind, string summary, DateOnly? nextFollowUp, CancellationToken ct = default)
    {
        var client = await _clients.GetAsync(id, ct);
        if (client is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return gate;
        client.AddActivity(_clock.UtcNow, Actor(), kind, summary, nextFollowUp);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome<Guid>> CreateContractAsync(Guid clientId, string name, DateOnly start, DateOnly end, decimal amount, string currency, PricingKind pricing, IEnumerable<Guid> vendors, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageClients);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var client = await _clients.GetAsync(clientId, ct);
        if (client is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var ready = client.CanEstablish();
        if (!ready.Ok)
            return Outcome<Guid>.Fail(ready.Code, ready.Message);
        var contract = Contract.Create(clientId, name, start, end, amount, currency, pricing, vendors);
        await _contracts.AddAsync(contract, ct);
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(contract.Id);
    }

    public async Task<Outcome<Guid>> CreateProjectAsync(Guid contractId, string name, DateOnly start, DateOnly targetEnd, RevenueMethod method, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageProjects);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var contract = await _contracts.GetAsync(contractId, ct);
        if (contract is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("合約"));
        var client = await _clients.GetAsync(contract.ClientId, ct);
        if (client is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var ready = client.CanEstablish();
        if (!ready.Ok)
            return Outcome<Guid>.Fail(ready.Code, ready.Message);
        var project = Project.Create(contractId, name, start, targetEnd, method, _clock.UtcNow);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.Established, $"成立專案 {project.Name}");
        await _projects.AddAsync(project, ct);
        if (client.Lifecycle != ClientLifecycle.Active)
        {
            client.MarkActive();
            client.AddActivity(_clock.UtcNow, Actor(), ClientActivityKind.ProjectEstablished, $"成立專案 {project.Name}", null);
        }
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(project.Id);
    }

    public async Task<Outcome<Guid>> EstablishAsync(EstablishProjectRequest request, CancellationToken ct = default)
    {
        var gate = _auth.Ensure(PlatformCapability.ManageProjects);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var client = await _clients.GetAsync(request.ClientId, ct);
        if (client is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("客戶"));
        var ready = client.CanEstablish();
        if (!ready.Ok)
            return Outcome<Guid>.Fail(ready.Code, ready.Message);

        Contract contract;
        if (request.ExistingContractId is Guid contractId)
        {
            var existing = await _contracts.GetAsync(contractId, ct);
            if (existing is null || existing.ClientId != client.Id)
                return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("合約"));
            contract = existing;
        }
        else
        {
            contract = Contract.Create(client.Id, request.ContractName, request.ContractStart, request.ContractEnd, request.Amount, request.Currency, request.Pricing, request.VendorIds);
            await _contracts.AddAsync(contract, ct);
        }

        var project = Project.Create(contract.Id, request.ProjectName, request.ProjectStart, request.ProjectEnd, request.Method, _clock.UtcNow);
        if (!request.StartActive)
            project.SetStatus(ProjectStatus.Draft);
        if (!string.IsNullOrWhiteSpace(request.OwnerRepo))
            project.AddRepo(request.OwnerRepo);
        if (!string.IsNullOrWhiteSpace(request.WorkspacePath) || request.StartActive)
            project.Update(project.Name, project.Start, project.TargetEnd, project.RevenueMethod, request.WorkspacePath, project.ExcludeFromMarginKpi, project.ClientRateCipher);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.Established, $"從客戶成立專案 {project.Name}");
        if (!string.IsNullOrWhiteSpace(request.OwnerRepo))
            project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.RepoLinked, $"掛上 {request.OwnerRepo.Trim()}");
        await _projects.AddAsync(project, ct);

        client.MarkActive();
        client.AddActivity(_clock.UtcNow, Actor(), ClientActivityKind.ProjectEstablished, $"成立專案 {project.Name}", null);
        await _uow.SaveChangesAsync(ct);
        await _audit.SensitiveChange(AuditActions.EstablishProject, "Project", project.Id, $"成立專案 {project.Name}", null, new { contract.Amount, contract.Currency }, ct);
        return Outcome<Guid>.Success(project.Id);
    }

    public async Task<Outcome> UpdateProjectAsync(Guid id, string name, DateOnly start, DateOnly targetEnd, RevenueMethod method, string? workspace, bool excludeMargin, decimal? clientRate, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(id, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: id);
        if (!gate.Ok)
            return gate;
        string? cipher = project.ClientRateCipher;
        if (clientRate is decimal rate)
        {
            var rateGate = _auth.Ensure(PlatformCapability.ViewClientRate, projectId: id);
            if (!rateGate.Ok)
                return rateGate;
            cipher = _encryptor.EncryptDecimal(rate);
        }
        project.Update(name, start, targetEnd, method, workspace, excludeMargin, cipher);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> CloseProjectAsync(Guid id, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(id, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: id);
        if (!gate.Ok)
            return gate;
        project.SetStatus(ProjectStatus.Closed);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.Closed, "專案結案");
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetProjectCodeAsync(Guid id, string? projectCode, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(id, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: id);
        if (!gate.Ok)
            return gate;
        try
        {
            project.SetProjectCode(projectCode);
        }
        catch (DomainException ex)
        {
            return Outcome.Fail(ex.Code, ex.Message);
        }
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> SetProjectStatusAsync(Guid id, ProjectStatus status, CancellationToken ct = default)
    {
        if (status == ProjectStatus.Closed)
            return await CloseProjectAsync(id, ct);
        var project = await _projects.GetAsync(id, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: id);
        if (!gate.Ok)
            return gate;
        if (project.Status == ProjectStatus.Closed)
            return Outcome.Fail(ErrorCodes.ProjectClosed, Messages.ProjectClosed);
        project.SetStatus(status);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.Note, $"狀態改為 {status}");
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> AddRepoAsync(Guid projectId, string ownerRepo, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: projectId);
        if (!gate.Ok)
            return gate;
        project.AddRepo(ownerRepo);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.RepoLinked, $"掛上 {ownerRepo.Trim()}");
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public Task<IReadOnlyList<string>> ListGithubReposAsync(CancellationToken ct = default) =>
        _github.ListReposAsync(ct);

    public Task<ProjectDocsCatalog> DocsCatalogAsync(Guid projectId, CancellationToken ct = default) =>
        LoadCatalogAsync(projectId, ct);

    public async Task<Outcome<Guid>> AddMilestoneAsync(Guid projectId, string name, DateOnly due, decimal amount, Guid? dependsOn, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome<Guid>.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: projectId);
        if (!gate.Ok)
            return Outcome<Guid>.Fail(gate.Code, gate.Message);
        var item = project.AddMilestone(name, due, amount, dependsOn);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.MilestoneAdded, $"加里程碑 {item.Name}");
        await _uow.SaveChangesAsync(ct);
        return Outcome<Guid>.Success(item.Id);
    }

    public async Task<Outcome> EnterPhaseAsync(Guid projectId, Guid phaseId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: projectId);
        if (!gate.Ok)
            return gate;
        var phase = project.Phases.FirstOrDefault(p => p.Id == phaseId);
        project.EnterPhase(phaseId, _clock.UtcNow);
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.PhaseEntered, $"進入階段 {phase?.Name ?? ""}");
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<Outcome> AddProjectNoteAsync(Guid projectId, string summary, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return Outcome.Fail(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var gate = _auth.Ensure(PlatformCapability.ManageProjects, projectId: projectId);
        if (!gate.Ok)
            return gate;
        project.AddJournal(_clock.UtcNow, Actor(), ProjectJournalKind.Note, summary);
        await _uow.SaveChangesAsync(ct);
        return Outcome.Success();
    }

    public async Task<GanttView> GanttAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct) ?? throw new DomainException(ErrorCodes.NotFound, Messages.NotFound("專案"));
        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var bars = new List<GanttBar>();
        foreach (var phase in project.Phases)
        {
            bars.Add(new GanttBar("phase", phase.Id, phase.Name, phase.PlanStart, phase.PlanEnd, phase.IsOverdue(today), phase.EnteredAt is DateTimeOffset e ? DateOnly.FromDateTime(e.UtcDateTime) : null, phase.LeftAt is DateTimeOffset l ? DateOnly.FromDateTime(l.UtcDateTime) : null));
        }
        foreach (var milestone in project.Milestones)
        {
            var depName = milestone.DependsOnId is Guid dep
                ? project.Milestones.FirstOrDefault(m => m.Id == dep)?.Name
                : null;
            bars.Add(new GanttBar("milestone", milestone.Id, milestone.Name, milestone.Due, milestone.Due, milestone.IsOverdue(today), milestone.Completed ? milestone.Due : null, null, depName));
        }
        var people = await _assignments.ListForProjectAsync(projectId, ct);
        IntakeSummary? intake = null;
        if (!string.IsNullOrWhiteSpace(project.WorkspacePath))
            intake = await _intake.ReadAsync(project.WorkspacePath, ct);
        return new GanttView(project.Id, project.Name, today, bars, people.Where(a => a.Status != AssignmentStatus.Cancelled).ToList(), intake);
    }

    async Task<ProjectDocsCatalog> LoadCatalogAsync(Guid projectId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return ProjectDocsCatalog.Empty(Messages.NotFound("專案"));
        try
        {
            return await _docs.ReadAsync(project, ct);
        }
        catch
        {
            return ProjectDocsCatalog.Empty("文件目錄暫時讀不到，不影響這一頁其他內容。", !string.IsNullOrWhiteSpace(project.WorkspacePath), project.WorkspacePath);
        }
    }

    string Actor() =>
        string.IsNullOrWhiteSpace(_user.DisplayName)
            ? (string.IsNullOrWhiteSpace(_user.UserName) ? _user.GitHubLogin : _user.UserName)
            : _user.DisplayName;
}

public sealed record EstablishProjectRequest(
    Guid ClientId,
    Guid? ExistingContractId,
    string ContractName,
    DateOnly ContractStart,
    DateOnly ContractEnd,
    decimal Amount,
    string Currency,
    PricingKind Pricing,
    IReadOnlyList<Guid> VendorIds,
    string ProjectName,
    DateOnly ProjectStart,
    DateOnly ProjectEnd,
    RevenueMethod Method,
    bool StartActive,
    string? OwnerRepo,
    string? WorkspacePath);

public sealed record GanttBar(string Kind, Guid Id, string Name, DateOnly Start, DateOnly End, bool Overdue, DateOnly? ActualStart, DateOnly? ActualEnd, string? DependsOnName = null);

public sealed record GanttView(Guid ProjectId, string Name, DateOnly Today, IReadOnlyList<GanttBar> Bars, IReadOnlyList<Assignment> Assignments, IntakeSummary? Intake);
