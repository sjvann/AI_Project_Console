using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public interface IPersonRepository
{
    Task<Person?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Person?> GetByGitHubAsync(string login, CancellationToken ct = default);
    Task<IReadOnlyList<Person>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Person person, CancellationToken ct = default);
}

public interface IVendorRepository
{
    Task<Vendor?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Vendor>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Vendor vendor, CancellationToken ct = default);
}

public interface IInvitationRepository
{
    Task<Invitation?> GetByGitHubAsync(string login, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Invitation invitation, CancellationToken ct = default);
}

public interface IClientRepository
{
    Task<Client?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Client>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Client client, CancellationToken ct = default);
}

public interface IContractRepository
{
    Task<Contract?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Contract>> ListByClientAsync(Guid clientId, CancellationToken ct = default);
    Task AddAsync(Contract contract, CancellationToken ct = default);
}

public interface IProjectRepository
{
    Task<Project?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Project?> GetByCodeAsync(string projectCode, CancellationToken ct = default);
    Task<Project?> FindByRepoAsync(string ownerRepo, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Project project, CancellationToken ct = default);
}

public interface IReportingApiKeyRepository
{
    Task<ReportingApiKey?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ReportingApiKey?> GetByHashAsync(string keyHash, CancellationToken ct = default);
    Task<IReadOnlyList<ReportingApiKey>> ListForPersonAsync(Guid personId, CancellationToken ct = default);
    Task AddAsync(ReportingApiKey key, CancellationToken ct = default);
}

public interface IAssignmentRepository
{
    Task<Assignment?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> ListForWeekAsync(DateOnly weekStart, CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> ListForPersonAsync(Guid personId, CancellationToken ct = default);
    Task<IReadOnlyList<Assignment>> ListForProjectAsync(Guid projectId, CancellationToken ct = default);
    Task AddAsync(Assignment assignment, CancellationToken ct = default);
}

public interface ITimesheetRepository
{
    Task<Timesheet?> GetByLocalSlotAsync(string localSlotId, CancellationToken ct = default);
    Task<Timesheet?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Timesheet>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Timesheet>> ListForPersonAsync(Guid personId, CancellationToken ct = default);
    Task<IReadOnlyList<Timesheet>> ListForPeriodAsync(DateOnly start, DateOnly end, CancellationToken ct = default);
    Task AddAsync(Timesheet timesheet, CancellationToken ct = default);
}

public interface IPayrollRepository
{
    Task<PayrollPeriod?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PayrollPeriod?> GetByRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default);
    Task<IReadOnlyList<PayrollPeriod>> ListAsync(CancellationToken ct = default);
    Task AddAsync(PayrollPeriod period, CancellationToken ct = default);
}

public interface ISettingsRepository
{
    Task<CompanySettings> GetAsync(CancellationToken ct = default);
}

public interface IUnmatchedUploadRepository
{
    Task<IReadOnlyList<UnmatchedUpload>> ListOpenAsync(CancellationToken ct = default);
    Task AddAsync(UnmatchedUpload item, CancellationToken ct = default);
}

public interface IStaffAccountRepository
{
    Task<StaffAccount?> GetAsync(Guid id, CancellationToken ct = default);
    Task<StaffAccount?> GetByUserNameAsync(string userName, CancellationToken ct = default);
    Task<IReadOnlyList<StaffAccount>> ListAsync(CancellationToken ct = default);
    Task AddAsync(StaffAccount account, CancellationToken ct = default);
    Task<int> CountEnabledOwnersAsync(CancellationToken ct = default);
}

public interface IWorkspaceIntakeReader
{
    Task<IntakeSummary?> ReadAsync(string workspacePath, CancellationToken ct = default);
}

public interface IProjectDocsCatalog
{
    Task<ProjectDocsCatalog> ReadAsync(Project project, CancellationToken ct = default);
}
