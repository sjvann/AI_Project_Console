using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiProject.Company.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCompanyInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CompanyDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connection = configuration.GetConnectionString("Company") ?? configuration["Company:ConnectionString"] ?? "";
            if (connection.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) || connection.Contains("Mode=Memory", StringComparison.OrdinalIgnoreCase) || connection.StartsWith("sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(NormalizeSqlite(connection));
            else
                options.UseNpgsql(connection);
        });
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IAuditLog, EfAuditLog>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IPersonRepository, PersonRepository>();
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IUnmatchedUploadRepository, UnmatchedUploadRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IContractRepository, ContractRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ITimesheetRepository, TimesheetRepository>();
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFieldEncryptor, AesFieldEncryptor>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddSingleton<LocalOwnerCredentials>();
        services.AddScoped<IStaffAccountRepository, StaffAccountRepository>();
        services.AddScoped<IReportingApiKeyRepository, ReportingApiKeyRepository>();
        services.AddHttpClient<IGitHubDirectory, GitHubDirectory>();
        services.AddScoped<IWorkspaceIntakeReader, WorkspaceIntakeReader>();
        services.AddHttpClient<IProjectDocsCatalog, GitHubProjectDocsCatalog>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUser>(sp => BuildCurrentUser(sp).GetAwaiter().GetResult());
        return services;
    }

    public static IServiceCollection AddCompanyPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCompanyInfrastructure(configuration);
        services.AddCompanyApplication();
        return services;
    }

    static string NormalizeSqlite(string connection)
    {
        if (connection.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase))
            return connection["sqlite:".Length..];
        return connection;
    }

    static async Task<ICurrentUser> BuildCurrentUser(IServiceProvider sp)
    {
        var tenant = sp.GetRequiredService<ITenantContext>();
        var http = sp.GetRequiredService<IHttpContextAccessor>().HttpContext;
        var principal = http?.User;
        var accountName = principal?.FindFirst("account")?.Value ?? "";
        if (!string.IsNullOrWhiteSpace(accountName))
        {
            var accounts = sp.GetRequiredService<IStaffAccountRepository>();
            var people = sp.GetRequiredService<IPersonRepository>();
            var assignments = sp.GetRequiredService<IAssignmentRepository>();
            var account = await accounts.GetByUserNameAsync(accountName);
            if (account is null || !account.Enabled)
                return new HttpCurrentUser();
            tenant.Assign(account.TenantId);
            var person = await people.GetAsync(account.PersonId);
            var projectIds = new HashSet<Guid>();
            if (person is not null)
            {
                foreach (var a in await assignments.ListForPersonAsync(person.Id))
                {
                    if (a.Status != AssignmentStatus.Cancelled)
                        projectIds.Add(a.ProjectId);
                }
            }
            return new HttpCurrentUser
            {
                IsAuthenticated = true,
                PersonId = person?.Id ?? account.PersonId,
                UserName = account.UserName,
                GitHubLogin = person?.GitHubLogin ?? "",
                DisplayName = person?.DisplayName ?? account.UserName,
                Role = account.Role,
                VendorId = person?.VendorId,
                AuthorizedProjectIds = projectIds,
            };
        }
        if (http is null)
            return new HttpCurrentUser();
        var bearer = http.Request.Headers.Authorization.ToString();
        if (!bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return new HttpCurrentUser();
        var token = bearer["Bearer ".Length..].Trim();
        if (ReportingApiKey.LooksLikeApiKey(token))
        {
            var apiUser = await BuildFromApiKeyAsync(sp, tenant, token);
            if (apiUser is not null)
                return apiUser;
            return new HttpCurrentUser();
        }
        var github = sp.GetRequiredService<IGitHubDirectory>();
        var login = (await github.ResolveLoginAsync(token) ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(login))
            return new HttpCurrentUser();
        var db = sp.GetRequiredService<CompanyDbContext>();
        var probePerson = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.GitHubLogin == login);
        var probeInvites = await db.Invitations.AsNoTracking()
            .Where(i => i.GitHubLogin == login && !i.Revoked)
            .ToListAsync();
        var probeInvite = probeInvites.OrderByDescending(i => i.InvitedAt).FirstOrDefault();
        if (probePerson is not null)
            tenant.Assign(probePerson.TenantId);
        else if (probeInvite is not null)
            tenant.Assign(probeInvite.TenantId);
        var peopleByGh = sp.GetRequiredService<IPersonRepository>();
        var invites = sp.GetRequiredService<IInvitationRepository>();
        var assignmentRepo = sp.GetRequiredService<IAssignmentRepository>();
        var personByGh = await peopleByGh.GetByGitHubAsync(login);
        var invite = await invites.GetByGitHubAsync(login);
        if (personByGh is null && invite is null)
            return new HttpCurrentUser { IsAuthenticated = true, UserName = login, GitHubLogin = login, DisplayName = login, Role = PlatformRole.Engineer };
        var role = invite?.Role ?? PlatformRole.Engineer;
        var ids = new HashSet<Guid>();
        if (personByGh is not null)
        {
            foreach (var a in await assignmentRepo.ListForPersonAsync(personByGh.Id))
            {
                if (a.Status != AssignmentStatus.Cancelled)
                    ids.Add(a.ProjectId);
            }
        }
        return new HttpCurrentUser
        {
            IsAuthenticated = true,
            PersonId = personByGh?.Id ?? invite?.PersonId,
            UserName = login,
            GitHubLogin = login,
            DisplayName = personByGh?.DisplayName ?? login,
            Role = role,
            VendorId = personByGh?.VendorId ?? invite?.VendorId,
            AuthorizedProjectIds = ids,
        };
    }

    static async Task<HttpCurrentUser?> BuildFromApiKeyAsync(IServiceProvider sp, ITenantContext tenant, string plaintext)
    {
        var keys = sp.GetRequiredService<IReportingApiKeyRepository>();
        var key = await keys.GetByHashAsync(ReportingApiKey.Hash(plaintext));
        if (key is null || !key.IsActive)
            return null;
        tenant.Assign(key.TenantId);
        var people = sp.GetRequiredService<IPersonRepository>();
        var assignments = sp.GetRequiredService<IAssignmentRepository>();
        var clock = sp.GetRequiredService<IClock>();
        var person = await people.GetAsync(key.PersonId);
        if (person is null)
            return null;
        key.Touch(clock.UtcNow);
        var uow = sp.GetRequiredService<IUnitOfWork>();
        await uow.SaveChangesAsync();
        var ids = new HashSet<Guid>();
        foreach (var a in await assignments.ListForPersonAsync(person.Id))
        {
            if (a.Status != AssignmentStatus.Cancelled)
                ids.Add(a.ProjectId);
        }
        var login = person.GitHubLogin ?? "";
        return new HttpCurrentUser
        {
            IsAuthenticated = true,
            PersonId = person.Id,
            UserName = key.KeyPrefix,
            GitHubLogin = login,
            DisplayName = person.DisplayName,
            Role = PlatformRole.Engineer,
            VendorId = person.VendorId,
            AuthorizedProjectIds = ids,
        };
    }
}
