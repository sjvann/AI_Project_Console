using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.EntityFrameworkCore;

namespace AiProject.Company.Infrastructure;

public sealed class EfUnitOfWork : IUnitOfWork
{
    readonly CompanyDbContext _db;
    public EfUnitOfWork(CompanyDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}

public sealed class EfAuditLog : IAuditLog
{
    readonly CompanyDbContext _db;
    public EfAuditLog(CompanyDbContext db) => _db = db;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        _db.Audits.Add(new AuditRow
        {
            Id = entry.Id,
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
        var query = _db.Audits.AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (entityId is Guid id)
            query = query.Where(a => a.EntityId == id);
        var rows = await query.Take(500).ToListAsync(cancellationToken);
        return rows.OrderByDescending(a => a.At).Select(a => new AuditEntry(a.Id, a.At, a.ActorPersonId, a.ActorLogin, a.Action, a.EntityType, a.EntityId, a.Reason, a.BeforeJson, a.AfterJson, a.IsSensitiveRead)).ToList();
    }
}

public sealed class SettingsRepository : ISettingsRepository
{
    readonly CompanyDbContext _db;
    public SettingsRepository(CompanyDbContext db) => _db = db;

    public async Task<CompanySettings> GetAsync(CancellationToken ct = default)
    {
        var row = await _db.Settings.Include(s => s.ExchangeRates).FirstOrDefaultAsync(ct);
        if (row is not null)
            return row;
        row = CompanySettings.CreateDefault();
        _db.Settings.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }
}

public sealed class PersonRepository : IPersonRepository
{
    readonly CompanyDbContext _db;
    public PersonRepository(CompanyDbContext db) => _db = db;
    public Task<Person?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.People.Include(p => p.Unavailable).FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<Person?> GetByGitHubAsync(string login, CancellationToken ct = default)
    {
        var key = login.Trim().ToLowerInvariant();
        return _db.People.Include(p => p.Unavailable).FirstOrDefaultAsync(p => p.GitHubLogin == key, ct);
    }
    public async Task<IReadOnlyList<Person>> ListAsync(CancellationToken ct = default) =>
        await _db.People.Include(p => p.Unavailable).Where(p => !p.IsDeleted).ToListAsync(ct);
    public Task AddAsync(Person person, CancellationToken ct = default)
    {
        _db.People.Add(person);
        return Task.CompletedTask;
    }
}

public sealed class VendorRepository : IVendorRepository
{
    readonly CompanyDbContext _db;
    public VendorRepository(CompanyDbContext db) => _db = db;
    public Task<Vendor?> GetAsync(Guid id, CancellationToken ct = default) => _db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
    public async Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken ct = default) =>
        await _db.Vendors.Where(v => !v.IsDeleted).ToListAsync(ct);
    public Task AddAsync(Vendor vendor, CancellationToken ct = default)
    {
        _db.Vendors.Add(vendor);
        return Task.CompletedTask;
    }
}

public sealed class InvitationRepository : IInvitationRepository
{
    readonly CompanyDbContext _db;
    public InvitationRepository(CompanyDbContext db) => _db = db;
    public async Task<Invitation?> GetByGitHubAsync(string login, CancellationToken ct = default)
    {
        var key = login.Trim().ToLowerInvariant();
        var rows = await _db.Invitations.Where(i => i.GitHubLogin == key && !i.Revoked).ToListAsync(ct);
        return rows.OrderByDescending(i => i.InvitedAt).FirstOrDefault();
    }
    public async Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.Invitations.ToListAsync(ct);
        return rows.OrderByDescending(i => i.InvitedAt).ToList();
    }
    public Task AddAsync(Invitation invitation, CancellationToken ct = default)
    {
        _db.Invitations.Add(invitation);
        return Task.CompletedTask;
    }
}

public sealed class UnmatchedUploadRepository : IUnmatchedUploadRepository
{
    readonly CompanyDbContext _db;
    public UnmatchedUploadRepository(CompanyDbContext db) => _db = db;
    public async Task<IReadOnlyList<UnmatchedUpload>> ListOpenAsync(CancellationToken ct = default)
    {
        var rows = await _db.UnmatchedUploads.Where(u => u.BoundPersonId == null).ToListAsync(ct);
        return rows.OrderByDescending(u => u.ReceivedAt).ToList();
    }
    public Task AddAsync(UnmatchedUpload item, CancellationToken ct = default)
    {
        _db.UnmatchedUploads.Add(item);
        return Task.CompletedTask;
    }
}

public sealed class ClientRepository : IClientRepository
{
    readonly CompanyDbContext _db;
    public ClientRepository(CompanyDbContext db) => _db = db;
    public Task<Client?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Clients.Include(c => c.Activities).FirstOrDefaultAsync(c => c.Id == id, ct);
    public async Task<IReadOnlyList<Client>> ListAsync(CancellationToken ct = default) =>
        await _db.Clients.Include(c => c.Activities).Where(c => !c.IsDeleted).ToListAsync(ct);
    public Task AddAsync(Client client, CancellationToken ct = default)
    {
        _db.Clients.Add(client);
        return Task.CompletedTask;
    }
}

public sealed class ContractRepository : IContractRepository
{
    readonly CompanyDbContext _db;
    public ContractRepository(CompanyDbContext db) => _db = db;
    public Task<Contract?> GetAsync(Guid id, CancellationToken ct = default) => _db.Contracts.FirstOrDefaultAsync(c => c.Id == id, ct);
    public async Task<IReadOnlyList<Contract>> ListAsync(CancellationToken ct = default) =>
        await _db.Contracts.Where(c => !c.IsDeleted).ToListAsync(ct);
    public async Task<IReadOnlyList<Contract>> ListByClientAsync(Guid clientId, CancellationToken ct = default) =>
        await _db.Contracts.Where(c => c.ClientId == clientId && !c.IsDeleted).ToListAsync(ct);
    public Task AddAsync(Contract contract, CancellationToken ct = default)
    {
        _db.Contracts.Add(contract);
        return Task.CompletedTask;
    }
}

public sealed class ProjectRepository : IProjectRepository
{
    readonly CompanyDbContext _db;
    public ProjectRepository(CompanyDbContext db) => _db = db;
    public Task<Project?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default) =>
        await _db.Projects.Include(p => p.Phases).Include(p => p.Milestones).Include(p => p.Repos).Include(p => p.OtherExpenses).Include(p => p.Journals)
            .Where(p => !p.IsDeleted).ToListAsync(ct);
    public Task AddAsync(Project project, CancellationToken ct = default)
    {
        _db.Projects.Add(project);
        return Task.CompletedTask;
    }
}

public sealed class AssignmentRepository : IAssignmentRepository
{
    readonly CompanyDbContext _db;
    public AssignmentRepository(CompanyDbContext db) => _db = db;
    public Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default) => _db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct);
    public async Task<IReadOnlyList<Assignment>> ListAsync(CancellationToken ct = default) => await _db.Assignments.ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> ListForWeekAsync(DateOnly weekStart, CancellationToken ct = default)
    {
        var end = weekStart.AddDays(6);
        return await _db.Assignments.Where(a => a.Start <= end && a.End >= weekStart).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<Assignment>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        await _db.Assignments.Where(a => a.PersonId == personId).ToListAsync(ct);
    public async Task<IReadOnlyList<Assignment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default) =>
        await _db.Assignments.Where(a => a.ProjectId == projectId).ToListAsync(ct);
    public Task AddAsync(Assignment assignment, CancellationToken ct = default)
    {
        _db.Assignments.Add(assignment);
        return Task.CompletedTask;
    }
}

public sealed class TimesheetRepository : ITimesheetRepository
{
    readonly CompanyDbContext _db;
    public TimesheetRepository(CompanyDbContext db) => _db = db;
    public Task<Timesheet?> GetByLocalSlotAsync(string localSlotId, CancellationToken ct = default) =>
        _db.Timesheets.Include(t => t.Chart).FirstOrDefaultAsync(t => t.LocalSlotId == localSlotId, ct);
    public Task<Timesheet?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Timesheets.Include(t => t.Chart).FirstOrDefaultAsync(t => t.Id == id, ct);
    public async Task<IReadOnlyList<Timesheet>> ListAsync(CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart).ToListAsync(ct);
    public async Task<IReadOnlyList<Timesheet>> ListForPersonAsync(Guid personId, CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart).Where(t => t.PersonId == personId).ToListAsync(ct);
    public async Task<IReadOnlyList<Timesheet>> ListForPeriodAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        await _db.Timesheets.Include(t => t.Chart).Where(t => t.WorkDate >= start && t.WorkDate <= end).ToListAsync(ct);
    public Task AddAsync(Timesheet timesheet, CancellationToken ct = default)
    {
        _db.Timesheets.Add(timesheet);
        return Task.CompletedTask;
    }
}

public sealed class PayrollRepository : IPayrollRepository
{
    readonly CompanyDbContext _db;
    public PayrollRepository(CompanyDbContext db) => _db = db;
    public Task<PayrollPeriod?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.PayrollPeriods.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<PayrollPeriod?> GetByRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default) =>
        _db.PayrollPeriods.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Start == start && p.End == end, ct);
    public async Task<IReadOnlyList<PayrollPeriod>> ListAsync(CancellationToken ct = default) =>
        await _db.PayrollPeriods.Include(p => p.Lines).OrderByDescending(p => p.Start).ToListAsync(ct);
    public Task AddAsync(PayrollPeriod period, CancellationToken ct = default)
    {
        _db.PayrollPeriods.Add(period);
        return Task.CompletedTask;
    }
}

public sealed class StaffAccountRepository : IStaffAccountRepository
{
    readonly CompanyDbContext _db;
    public StaffAccountRepository(CompanyDbContext db) => _db = db;
    public Task<StaffAccount?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.StaffAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
    public Task<StaffAccount?> GetByUserNameAsync(string userName, CancellationToken ct = default)
    {
        var key = StaffAccount.NormalizeUserName(userName);
        return _db.StaffAccounts.FirstOrDefaultAsync(a => a.UserName == key, ct);
    }
    public async Task<IReadOnlyList<StaffAccount>> ListAsync(CancellationToken ct = default) =>
        await _db.StaffAccounts.OrderBy(a => a.UserName).ToListAsync(ct);
    public Task AddAsync(StaffAccount account, CancellationToken ct = default)
    {
        _db.StaffAccounts.Add(account);
        return Task.CompletedTask;
    }
    public Task<int> CountEnabledOwnersAsync(CancellationToken ct = default) =>
        _db.StaffAccounts.CountAsync(a => a.Role == PlatformRole.Owner && a.Enabled, ct);
}
