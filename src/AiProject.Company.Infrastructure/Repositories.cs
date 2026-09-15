using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiProject.Company.Infrastructure;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public bool IsAssigned { get; private set; }

    public void Assign(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(ErrorCodes.Required, Messages.Required("租戶"));
        TenantId = tenantId;
        IsAssigned = true;
    }
}

public sealed class EfUnitOfWork : IUnitOfWork
{
    readonly CompanyDbContext _db;
    public EfUnitOfWork(CompanyDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}

public sealed class EfAuditLog : IAuditLog
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public EfAuditLog(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _db.Audits.Add(new AuditRow
        {
            Id = entry.Id,
            TenantId = _tenant.TenantId,
            At = entry.At,
            ActorPersonId = entry.ActorPersonId,
            ActorLogin = entry.ActorLogin,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Reason = entry.Reason,
            BeforeJson = entry.BeforeJson,
            AfterJson = entry.AfterJson,
            IsSensitiveRead = entry.IsSensitiveRead,
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(string? entityType, Guid? entityId, CancellationToken cancellationToken = default)
    {
        var query = _db.Audits.Where(a => a.TenantId == _tenant.TenantId);
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (entityId is Guid id)
            query = query.Where(a => a.EntityId == id);
        var rows = await query.Take(500).ToListAsync(cancellationToken);
        return rows.OrderByDescending(a => a.At).Select(a => new AuditEntry(a.Id, a.At, a.ActorPersonId, a.ActorLogin, a.Action, a.EntityType, a.EntityId, a.Reason, a.BeforeJson, a.AfterJson, a.IsSensitiveRead)).ToList();
    }
}

static class TenantRepo
{
    public static T? Guard<T>(T? row, ITenantContext tenant) where T : class, ITenantScoped
    {
        if (row is null)
            return null;
        TenantGuards.EnsureBound(row, tenant.TenantId);
        return row;
    }
}

public sealed class SettingsRepository : ISettingsRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public SettingsRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<CompanySettings> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.Settings.Include(s => s.ExchangeRates)
            .FirstOrDefaultAsync(s => s.TenantId == _tenant.TenantId, ct);
        if (row is not null)
            return row;
        row = CompanySettings.CreateForTenant(_tenant.TenantId);
        _db.Settings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }
}

public sealed class PersonRepository : IPersonRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public PersonRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Person?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await _db.People.Include(p => p.Unavailable).FirstOrDefaultAsync(p => p.Id == id, ct);
        return TenantRepo.Guard(row, _tenant);
    }

    public Task<Person?> GetByGitHubAsync(string login, CancellationToken ct = default)
    {
        var key = login.Trim().ToLowerInvariant();
        // 登入解析可跨庫找人，再由呼叫端 Assign 租戶；查到後仍驗證目前租戶。
        return GetByGitHubInTenantAsync(key, ct);
    }

    async Task<Person?> GetByGitHubInTenantAsync(string key, CancellationToken ct)
    {
        var row = await _db.People.Include(p => p.Unavailable)
            .FirstOrDefaultAsync(p => p.GitHubLogin == key && p.TenantId == _tenant.TenantId, ct);
        return row;
    }

    public async Task<IReadOnlyList<Person>> ListAsync(CancellationToken ct = default) =>
        await _db.People.Include(p => p.Unavailable)
            .Where(p => !p.IsDeleted && p.TenantId == _tenant.TenantId).ToListAsync(ct);

    public Task AddAsync(Person person, CancellationToken ct = default)
    {
        person.BindTenant(_tenant.TenantId);
        _db.People.Add(person);
        return Task.CompletedTask;
    }
}

public sealed class VendorRepository : IVendorRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public VendorRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Vendor?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct), _tenant);

    public async Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken ct = default) =>
        await _db.Vendors.Where(v => !v.IsDeleted && v.TenantId == _tenant.TenantId).ToListAsync(ct);

    public Task AddAsync(Vendor vendor, CancellationToken ct = default)
    {
        vendor.BindTenant(_tenant.TenantId);
        _db.Vendors.Add(vendor);
        return Task.CompletedTask;
    }
}

public sealed class InvitationRepository : IInvitationRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public InvitationRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Invitation?> GetByGitHubAsync(string login, CancellationToken ct = default)
    {
        var key = login.Trim().ToLowerInvariant();
        var rows = await _db.Invitations
            .Where(i => i.GitHubLogin == key && !i.Revoked && i.TenantId == _tenant.TenantId)
            .ToListAsync(ct);
        return rows.OrderByDescending(i => i.InvitedAt).FirstOrDefault();
    }

    public async Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Invitations.Where(i => i.TenantId == _tenant.TenantId).ToListAsync(ct);
        return rows.OrderByDescending(i => i.InvitedAt).ToList();
    }

    public Task AddAsync(Invitation invitation, CancellationToken ct = default)
    {
        invitation.BindTenant(_tenant.TenantId);
        _db.Invitations.Add(invitation);
        return Task.CompletedTask;
    }
}

public sealed class UnmatchedUploadRepository : IUnmatchedUploadRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public UnmatchedUploadRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<UnmatchedUpload>> ListOpenAsync(CancellationToken ct = default)
    {
        var rows = await _db.UnmatchedUploads
            .Where(u => u.BoundPersonId == null && u.TenantId == _tenant.TenantId).ToListAsync(ct);
        return rows.OrderByDescending(u => u.ReceivedAt).ToList();
    }

    public Task AddAsync(UnmatchedUpload item, CancellationToken ct = default)
    {
        item.BindTenant(_tenant.TenantId);
        _db.UnmatchedUploads.Add(item);
        return Task.CompletedTask;
    }
}

public sealed class ClientRepository : IClientRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public ClientRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Client?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.Clients.Include(c => c.Activities).FirstOrDefaultAsync(c => c.Id == id, ct), _tenant);

    public async Task<IReadOnlyList<Client>> ListAsync(CancellationToken ct = default) =>
        await _db.Clients.Include(c => c.Activities)
            .Where(c => !c.IsDeleted && c.TenantId == _tenant.TenantId).ToListAsync(ct);

    public Task AddAsync(Client client, CancellationToken ct = default)
    {
        client.BindTenant(_tenant.TenantId);
        _db.Clients.Add(client);
        return Task.CompletedTask;
    }
}

public sealed class ContractRepository : IContractRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public ContractRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Contract?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct), _tenant);

    public async Task<IReadOnlyList<Contract>> ListAsync(CancellationToken ct = default) =>
        await _db.Contracts.Where(c => !c.IsDeleted && c.TenantId == _tenant.TenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<Contract>> ListByClientAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Contracts.Where(c => c.ClientId == clientId && !c.IsDeleted && c.TenantId == _tenant.TenantId).ToListAsync(ct);

    public Task AddAsync(Contract contract, CancellationToken ct = default)
    {
        contract.BindTenant(_tenant.TenantId);
        _db.Contracts.Add(contract);
        return Task.CompletedTask;
    }
}

public sealed class ProjectRepository : IProjectRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public ProjectRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Project?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(
            await _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
                .FirstOrDefaultAsync(p => p.Id == id, ct),
            _tenant);

    public async Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct = default)
    {
        var code = projectCode.Trim();
        if (string.IsNullOrEmpty(code))
            return null;
        return await _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.TenantId == _tenant.TenantId && p.ProjectCode == code, ct);
    }

    public async Task<Project?> FindByRepoAsync(string ownerRepo, CancellationToken ct = default)
    {
        var slug = ownerRepo.Trim();
        if (string.IsNullOrEmpty(slug))
            return null;
        var projects = await _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
            .Where(p => !p.IsDeleted && p.TenantId == _tenant.TenantId)
            .ToListAsync(ct);
        return projects.FirstOrDefault(p => p.Repos.Any(r => string.Equals(r.OwnerRepo, slug, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default) =>
        await _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
            .Where(p => !p.IsDeleted && p.TenantId == _tenant.TenantId).ToListAsync(ct);

    public Task AddAsync(Project project, CancellationToken ct = default)
    {
        project.BindTenant(_tenant.TenantId);
        _db.Projects.Add(project);
        return Task.CompletedTask;
    }
}

public sealed class AssignmentRepository : IAssignmentRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public AssignmentRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct), _tenant);

    public async Task<IReadOnlyList<Assignment>> ListAsync(CancellationToken ct = default) =>
        await _db.Assignments.Where(a => a.TenantId == _tenant.TenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<Assignment>> ListForWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        var end = weekStart.AddDays(6);
        return await _db.Assignments
            .Where(a => a.TenantId == _tenant.TenantId && a.Start <= end && a.End >= weekStart).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Assignment>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        await _db.Assignments.Where(a => a.TenantId == _tenant.TenantId && a.PersonId == personId).ToListAsync(ct);

    public async Task<IReadOnlyList<Assignment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        await _db.Assignments.Where(a => a.TenantId == _tenant.TenantId && a.ProjectId == projectId).ToListAsync(ct);

    public Task AddAsync(Assignment assignment, CancellationToken ct = default)
    {
        assignment.BindTenant(_tenant.TenantId);
        _db.Assignments.Add(assignment);
        return Task.CompletedTask;
    }
}

public sealed class TimesheetRepository : ITimesheetRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public TimesheetRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Timesheet?> GetByLocalSlotAsync(string localSlotId, CancellationToken ct = default) =>
        TenantRepo.Guard(
            await _db.Timesheets.Include(t => t.Chart)
                .FirstOrDefaultAsync(t => t.LocalSlotId == localSlotId, ct),
            _tenant);

    public async Task<Timesheet?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.Timesheets.Include(t => t.Chart).FirstOrDefaultAsync(t => t.Id == id, ct), _tenant);

    public async Task<IReadOnlyList<Timesheet>> ListAsync(CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart).Where(t => t.TenantId == _tenant.TenantId).ToListAsync(ct);

    public async Task<IReadOnlyList<Timesheet>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart)
            .Where(t => t.TenantId == _tenant.TenantId && t.PersonId == personId).ToListAsync(ct);

    public async Task<IReadOnlyList<Timesheet>> ListForPeriodAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart)
            .Where(t => t.TenantId == _tenant.TenantId && t.WorkDate >= start && t.WorkDate <= end).ToListAsync(ct);

    public Task AddAsync(Timesheet timesheet, CancellationToken ct = default)
    {
        timesheet.BindTenant(_tenant.TenantId);
        _db.Timesheets.Add(timesheet);
        return Task.CompletedTask;
    }
}

public sealed class PayrollRepository : IPayrollRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public PayrollRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<PayrollPeriod?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.PayrollPeriods.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct), _tenant);

    public async Task<PayrollPeriod?> GetByRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        await _db.PayrollPeriods.Include(p => p.Lines)
            .FirstOrDefaultAsync(p => p.TenantId == _tenant.TenantId && p.Start == start && p.End == end, ct);

    public async Task<IReadOnlyList<PayrollPeriod>> ListAsync(CancellationToken ct = default) =>
        await _db.PayrollPeriods.Include(p => p.Lines)
            .Where(p => p.TenantId == _tenant.TenantId)
            .OrderByDescending(p => p.Start).ToListAsync(ct);

    public Task AddAsync(PayrollPeriod period, CancellationToken ct = default)
    {
        period.BindTenant(_tenant.TenantId);
        _db.PayrollPeriods.Add(period);
        return Task.CompletedTask;
    }
}

public sealed class StaffAccountRepository : IStaffAccountRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public StaffAccountRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<StaffAccount?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.StaffAccounts.FirstOrDefaultAsync(a => a.Id == id, ct), _tenant);

    /// <summary>登入用：帳號名全域查找（再由呼叫端 Assign 租戶）。</summary>
    public Task<StaffAccount?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        var key = StaffAccount.NormalizeUserName(userName);
        return _db.StaffAccounts.FirstOrDefaultAsync(a => a.UserName == key, ct);
    }

    public async Task<IReadOnlyList<StaffAccount>> ListAsync(CancellationToken ct = default) =>
        await _db.StaffAccounts.Where(a => a.TenantId == _tenant.TenantId).OrderBy(a => a.UserName).ToListAsync(ct);

    public Task AddAsync(StaffAccount account, CancellationToken ct = default)
    {
        account.BindTenant(_tenant.TenantId);
        _db.StaffAccounts.Add(account);
        return Task.CompletedTask;
    }

    public Task<int> CountEnabledOwnersAsync(CancellationToken ct = default) =>
        _db.StaffAccounts.CountAsync(a => a.TenantId == _tenant.TenantId && a.Role == PlatformRole.Owner && a.Enabled, ct);
}

public sealed class ReportingApiKeyRepository : IReportingApiKeyRepository
{
    readonly CompanyDbContext _db;
    readonly ITenantContext _tenant;
    public ReportingApiKeyRepository(CompanyDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<ReportingApiKey?> GetAsync(Guid id, CancellationToken ct = default) =>
        TenantRepo.Guard(await _db.ReportingApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct), _tenant);

    /// <summary>認證用：以雜湊全域查找（再 Assign 租戶）。</summary>
    public Task<ReportingApiKey?> GetByHashAsync(string keyHash, CancellationToken ct = default) =>
        _db.ReportingApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.RevokedAt == null, ct);

    public async Task<IReadOnlyList<ReportingApiKey>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        await _db.ReportingApiKeys
            .Where(k => k.TenantId == _tenant.TenantId && k.PersonId == personId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);

    public Task AddAsync(ReportingApiKey key, CancellationToken ct = default)
    {
        key.BindTenant(_tenant.TenantId);
        _db.ReportingApiKeys.Add(key);
        return Task.CompletedTask;
    }
}
