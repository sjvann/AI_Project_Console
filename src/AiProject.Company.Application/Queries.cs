using AiProject.Company.Contracts;
using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class MeQueries
{
    readonly IAssignmentRepository _assignments;
    readonly ITimesheetRepository _timesheets;
    readonly IProjectRepository _projects;
    readonly IPayrollRepository _periods;
    readonly IAuthorizationGate _auth;
    readonly ICurrentUser _user;
    readonly ITenantContext _tenant;
    readonly PayrollCommands _payroll;
    readonly IClock _clock;
    readonly ISettingsRepository _settings;

    public MeQueries(
        IAssignmentRepository assignments,
        ITimesheetRepository timesheets,
        IProjectRepository projects,
        IPayrollRepository periods,
        IAuthorizationGate auth,
        ICurrentUser user,
        ITenantContext tenant,
        PayrollCommands payroll,
        IClock clock,
        ISettingsRepository settings)
    {
        _assignments = assignments;
        _timesheets = timesheets;
        _projects = projects;
        _periods = periods;
        _auth = auth;
        _user = user;
        _tenant = tenant;
        _payroll = payroll;
        _clock = clock;
        _settings = settings;
    }

    public async Task<IReadOnlyList<AssignmentDto>> MyAssignmentsAsync(CancellationToken ct = default)
    {
        if (_user.PersonId is not Guid personId)
            return [];
        var projects = (await _projects.ListAsync(ct)).ToDictionary(p => p.Id);
        var items = await _assignments.ListForPersonAsync(personId, ct);
        return items.Where(a => a.Status != AssignmentStatus.Cancelled).Select(a =>
        {
            projects.TryGetValue(a.ProjectId, out var p);
            return new AssignmentDto
            {
                AssignmentId = a.Id,
                ProjectId = a.ProjectId,
                ProjectName = p?.Name ?? a.ProjectId.ToString(),
                Start = a.Start,
                End = a.End,
                HoursPerWeek = a.PlannedHoursPerWeek,
                Role = a.Role.ToString(),
                SyncState = a.SyncState.ToString(),
                Repos = p is null ? [] : p.Repos.Select(r => r.OwnerRepo).ToList(),
            };
        }).ToList();
    }

    public async Task<MyHoursView> MyHoursAsync(CancellationToken ct = default)
    {
        if (_user.PersonId is not Guid personId)
            return new MyHoursView([], "繪圖請用控制台。你的 GitHub 帳號尚未對到人員檔，請找人資。");
        var sheets = await _timesheets.ListForPersonAsync(personId, ct);
        var projects = (await _projects.ListAsync(ct)).ToDictionary(p => p.Id);
        var rows = sheets.OrderByDescending(s => s.WorkDate).Select(s => new MyHoursRow(
            s.Id,
            s.WorkDate,
            projects.TryGetValue(s.ProjectId, out var p) ? p.Name : "",
            s.Hours,
            s.Status,
            s.ReturnReason,
            s.Chart)).ToList();
        return new MyHoursView(rows, "到控制台畫狀態並上傳");
    }

    public async Task<PayslipDto?> LatestPayslipAsync(CancellationToken ct = default)
    {
        if (_user.PersonId is not Guid personId)
            return null;
        var periods = await _periods.ListAsync(ct);
        var latest = periods.OrderByDescending(p => p.Start).FirstOrDefault();
        if (latest is null)
            return null;
        return await _payroll.PayslipAsync(personId, latest.Id, ct);
    }

    /// <summary>公開回報契約握手（A2-1）：確認呼叫者可否對此租戶申報。</summary>
    public Task<MeDto> HandshakeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_user.IsAuthenticated || string.IsNullOrWhiteSpace(_user.GitHubLogin) && string.IsNullOrWhiteSpace(_user.UserName))
        {
            return Task.FromResult(new MeDto
            {
                Matched = false,
                Message = "請先用 GitHub 權杖或公司帳戶登入後再測試連線。",
            });
        }

        var login = string.IsNullOrWhiteSpace(_user.GitHubLogin) ? _user.UserName : _user.GitHubLogin;
        if (_user.PersonId is Guid personId)
        {
            return Task.FromResult(new MeDto
            {
                PersonId = personId,
                TenantId = _tenant.TenantId,
                GitHubLogin = login,
                DisplayName = _user.DisplayName,
                Role = _user.Role.ToString(),
                Matched = true,
                Message = "已對到人員，可以申報工時。",
            });
        }

        return Task.FromResult(new MeDto
        {
            TenantId = _tenant.TenantId,
            GitHubLogin = login,
            DisplayName = _user.DisplayName,
            Role = _user.Role.ToString(),
            Matched = false,
            Message = "GitHub 帳號尚未對到本工作區人員檔。請找人資加入名冊，或至「待歸戶」處理。",
        });
    }
}

public sealed record MyHoursRow(Guid Id, DateOnly Date, string ProjectName, decimal Hours, TimesheetStatus Status, string? ReturnReason, IReadOnlyList<StatusChartCell> Chart);

public sealed record MyHoursView(IReadOnlyList<MyHoursRow> Rows, string DrawHint);

public sealed class DirectoryQueries
{
    readonly IPersonRepository _people;
    readonly IVendorRepository _vendors;
    readonly IClientRepository _clients;
    readonly IContractRepository _contracts;
    readonly IProjectRepository _projects;
    readonly IAssignmentRepository _assignments;
    readonly ITimesheetRepository _timesheets;
    readonly IInvitationRepository _invites;
    readonly IUnmatchedUploadRepository _unmatched;
    readonly IPayrollRepository _periods;
    readonly IAuthorizationGate _auth;
    readonly IAvailabilityCalculator _availability;
    readonly IClock _clock;
    readonly ISettingsRepository _settings;
    readonly ICurrentUser _user;
    readonly IGitHubDirectory _github;
    readonly IStaffAccountRepository _accounts;

    public DirectoryQueries(
        IPersonRepository people,
        IVendorRepository vendors,
        IClientRepository clients,
        IContractRepository contracts,
        IProjectRepository projects,
        IAssignmentRepository assignments,
        ITimesheetRepository timesheets,
        IInvitationRepository invites,
        IUnmatchedUploadRepository unmatched,
        IPayrollRepository periods,
        IAuthorizationGate auth,
        IAvailabilityCalculator availability,
        IClock clock,
        ISettingsRepository settings,
        ICurrentUser user,
        IGitHubDirectory github,
        IStaffAccountRepository accounts)
    {
        _people = people;
        _vendors = vendors;
        _clients = clients;
        _contracts = contracts;
        _projects = projects;
        _assignments = assignments;
        _timesheets = timesheets;
        _invites = invites;
        _unmatched = unmatched;
        _periods = periods;
        _auth = auth;
        _availability = availability;
        _clock = clock;
        _settings = settings;
        _user = user;
        _github = github;
        _accounts = accounts;
    }

    public async Task<IReadOnlyList<Person>> PeopleAsync(CancellationToken ct = default)
    {
        var all = await _people.ListAsync(ct);
        if (_user.Role == PlatformRole.VendorAdmin && _user.VendorId is Guid vendor)
            return all.Where(p => p.VendorId == vendor).ToList();
        if (_user.Role is PlatformRole.Engineer or PlatformRole.VendorEngineer)
            return all.Where(p => p.Id == _user.PersonId).ToList();
        return all;
    }

    public async Task<IReadOnlyList<Vendor>> VendorsAsync(CancellationToken ct = default)
    {
        var all = await _vendors.ListAsync(ct);
        if (_user.Role == PlatformRole.VendorAdmin && _user.VendorId is Guid vendor)
            return all.Where(v => v.Id == vendor).ToList();
        return all;
    }

    public Task<IReadOnlyList<Client>> ClientsAsync(CancellationToken ct = default) =>
        CanSeeClients() ? _clients.ListAsync(ct) : Task.FromResult<IReadOnlyList<Client>>([]);

    public async Task<Client?> ClientAsync(Guid id, CancellationToken ct = default) =>
        CanSeeClients() ? await _clients.GetAsync(id, ct) : null;

    public Task<IReadOnlyList<Contract>> ContractsAsync(CancellationToken ct = default) =>
        CanSeeClients() ? _contracts.ListAsync(ct) : Task.FromResult<IReadOnlyList<Contract>>([]);
    public Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken ct = default) => LoadProjectsAsync(false, ct);

    public Task<IReadOnlyList<Project>> AssignableProjectsAsync(CancellationToken ct = default) => LoadProjectsAsync(true, ct);

    async Task<IReadOnlyList<Project>> LoadProjectsAsync(bool assignableOnly, CancellationToken ct)
    {
        var all = await _projects.ListAsync(ct);
        if (_user.Role is PlatformRole.VendorAdmin or PlatformRole.VendorEngineer or PlatformRole.Engineer)
            all = all.Where(p => _user.AuthorizedProjectIds.Contains(p.Id)).ToList();
        if (!assignableOnly)
            return all;
        var contracts = (await _contracts.ListAsync(ct)).ToDictionary(c => c.Id);
        var clients = (await _clients.ListAsync(ct)).ToDictionary(c => c.Id);
        return all.Where(p =>
        {
            if (p.IsDeleted || p.Status is ProjectStatus.Closed or ProjectStatus.Draft)
                return false;
            if (!contracts.TryGetValue(p.ContractId, out var contract))
                return false;
            return clients.TryGetValue(contract.ClientId, out var client) && client.CountsAsActiveDelivery;
        }).ToList();
    }
    public Task<IReadOnlyList<Invitation>> InvitesAsync(CancellationToken ct = default) => _invites.ListAsync(ct);
    public Task<IReadOnlyList<StaffAccount>> StaffAccountsAsync(CancellationToken ct = default) => _accounts.ListAsync(ct);
    public Task<IReadOnlyList<UnmatchedUpload>> UnmatchedAsync(CancellationToken ct = default) => _unmatched.ListOpenAsync(ct);
    public Task<IReadOnlyList<Assignment>> AssignmentsAsync(CancellationToken ct = default) => _assignments.ListAsync(ct);

    public async Task<IReadOnlyList<PayrollPeriod>> PeriodsAsync(CancellationToken ct = default)
    {
        if (!_auth.Can(PlatformCapability.LockPayroll) && !_auth.Can(PlatformCapability.ViewSalary))
            return [];
        return await _periods.ListAsync(ct);
    }

    public async Task<IReadOnlyList<Timesheet>> TimesheetsAsync(CancellationToken ct = default)
    {
        var all = await _timesheets.ListAsync(ct);
        if (_auth.Can(PlatformCapability.ConfirmTimesheet) || _auth.Can(PlatformCapability.LockPayroll))
            return all;
        if (_user.PersonId is Guid pid)
            return all.Where(t => t.PersonId == pid).ToList();
        return [];
    }

    public async Task<IReadOnlyList<PersonAvailability>> WeekAvailabilityAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        var people = await PeopleAsync(ct);
        var assignments = await _assignments.ListForWeekAsync(weekStart, ct);
        return people.Select(p => _availability.ForWeek(p, weekStart, assignments)).ToList();
    }

    public async Task<IReadOnlyList<GitHubIssueRef>> UnassignedIssuesAsync(CancellationToken ct = default)
    {
        var projects = await AssignableProjectsAsync(ct);
        var list = new List<GitHubIssueRef>();
        foreach (var project in projects.Where(p => p.Status != ProjectStatus.Closed))
        {
            foreach (var repo in project.Repos)
            {
                var issues = await _github.ListOpenIssuesAsync(repo.OwnerRepo, ct);
                list.AddRange(issues.Where(i => string.IsNullOrWhiteSpace(i.AssigneeLogin)));
            }
        }
        return list;
    }

    public async Task<Contract?> ContractAsync(Guid id, CancellationToken ct = default) => await _contracts.GetAsync(id, ct);
    public async Task<Project?> ProjectAsync(Guid id, CancellationToken ct = default) => await _projects.GetAsync(id, ct);
    public async Task<Person?> PersonAsync(Guid id, CancellationToken ct = default) => await _people.GetAsync(id, ct);
    public async Task<CompanySettings> SettingsAsync(CancellationToken ct = default) => await _settings.GetAsync(ct);

    public DateOnly Today()
    {
        var tz = _settings.GetAsync().GetAwaiter().GetResult().TimeZoneId;
        return _clock.Today(tz);
    }

    bool CanSeeClients() =>
        _user.Role is not (PlatformRole.VendorAdmin or PlatformRole.VendorEngineer or PlatformRole.Engineer);
}
