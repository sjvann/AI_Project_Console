using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AiProject.Company.Application;
using AiProject.Company.Contracts;
using AiProject.Company.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiProject.Company.Web.Tests;

sealed class FakeGitHub : IGitHubDirectory
{
    public Task<IReadOnlyList<GitHubMember>> ListOrgMembersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitHubMember>>([new("alice", "Alice")]);
    public Task<IReadOnlyList<string>> ListReposAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(["acme/app"]);
    public Task<IReadOnlyList<GitHubIssueRef>> ListOpenIssuesAsync(string ownerRepo, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitHubIssueRef>>([new(ownerRepo, 1, "未派", null, DateTimeOffset.UtcNow.AddDays(-4), "https://example")]);
    public Task<Outcome> AddAssigneeAsync(string ownerRepo, int issueNumber, string login, CancellationToken cancellationToken = default) =>
        Task.FromResult(Outcome.Fail("github_writeback", "寫回 GitHub assignee 失敗，已標待同步。"));
    public Task<string?> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var token = accessToken.Trim();
        if (token.EndsWith("-token", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<string?>(token[..^"-token".Length].ToLowerInvariant());
        return Task.FromResult<string?>(token.ToLowerInvariant());
    }
}

public sealed class CompanyApiFactory : WebApplicationFactory<Program>
{
    readonly string _db = Path.Combine(Path.GetTempPath(), "company-web-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Company", "Data Source=" + _db);
        builder.UseSetting("Company:EncryptionKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        builder.UseSetting("Company:Auth:LocalOwner:Enabled", "true");
        builder.UseSetting("Company:Auth:LocalOwner:UserName", "owner");
        builder.UseSetting("Company:Auth:LocalOwner:Password", "AiProject-Owner-2026");
        builder.UseSetting("Company:Auth:LocalOwner:DisplayName", "公司管理員");
        builder.UseSetting("Company:SeedDemoData", "false");
        builder.UseSetting("Hosting:Mode", "SelfHosted");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Company"] = "Data Source=" + _db,
                ["Company:EncryptionKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                ["Company:Auth:LocalOwner:Enabled"] = "true",
                ["Company:Auth:LocalOwner:UserName"] = "owner",
                ["Company:Auth:LocalOwner:Password"] = "AiProject-Owner-2026",
                ["Company:Auth:LocalOwner:DisplayName"] = "公司管理員",
                ["Company:SeedDemoData"] = "false",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGitHubDirectory>();
            services.AddSingleton<IGitHubDirectory, FakeGitHub>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { File.Delete(_db); } catch { }
    }
}

public class ApiTests : IClassFixture<CompanyApiFactory>
{
    readonly CompanyApiFactory _factory;
    public ApiTests(CompanyApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_ok()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
        Assert.Contains("ok", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SelfHosted", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Default_tenant_is_seeded()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiProject.Company.Infrastructure.CompanyDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == TenantIds.Default);
        Assert.Equal("Default", tenant.DisplayName);
        var person = await db.People.FirstAsync();
        Assert.Equal(TenantIds.Default, person.TenantId);
    }

    [Fact]
    public async Task Login_page_has_no_github_oauth()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/login");
        Assert.Contains("公司帳戶", html);
        Assert.DoesNotContain("用 GitHub 登入", html);
        var github = await client.GetAsync("/login/github");
        Assert.True(github.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        var dev = await client.PostAsync("/login/dev", new FormUrlEncodedContent(new Dictionary<string, string> { ["login"] = "stranger" }));
        Assert.True(dev.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Handshake_me_matched_for_invited_engineer()
    {
        await SeedInvitedEngineerProjectAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.GetAsync("/api/v1/me");
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
        var me = await res.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(me);
        Assert.True(me.Matched);
        Assert.Equal("invited", me.GitHubLogin);
        Assert.NotNull(me.PersonId);
        Assert.Contains("申報", me.Message);
    }

    [Fact]
    public async Task Handshake_me_unmatched_is_human_readable()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "stranger-token");
        var res = await client.GetAsync("/api/v1/me");
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
        var me = await res.Content.ReadFromJsonAsync<MeDto>();
        Assert.NotNull(me);
        Assert.False(me.Matched);
        Assert.Equal("stranger", me.GitHubLogin);
        Assert.Contains("人資", me.Message);
    }

    [Fact]
    public async Task Handshake_me_without_auth_is_401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var error = await res.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.Unauthenticated, error?.Code);
        Assert.Contains("權杖", error?.Message);
    }

    [Fact]
    public async Task Upload_without_auth_is_401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest { LocalSlotId = "x", Hours = 1, WorkDate = new DateOnly(2026, 9, 1) });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Upload_unmatched_github_goes_to_queue()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bob-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-bob",
            ProjectId = Guid.NewGuid(),
            WorkDate = new DateOnly(2026, 9, 1),
            Hours = 8,
        });
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var error = await res.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.UnmatchedPerson, error?.Code);
        Assert.Contains("人資", error?.Message);
    }

    [Fact]
    public async Task Source_path_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "alice-token");
        var json = """{"localSlotId":"s","projectId":"00000000-0000-0000-0000-000000000001","workDate":"2026-09-01","hours":1,"sourcePath":"C:\\\\repo"}""";
        var res = await client.PostAsync("/api/v1/timesheets/upload", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Hr_cannot_open_war_room_or_budget()
    {
        var client = _factory.CreateClient();
        await SeedHrAsync();
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "hr1",
            ["password"] = "Hr-Pass-2026",
        }));
        Assert.True(login.IsSuccessStatusCode || login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        var page = await client.GetAsync("/war-room");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.Contains("你看不到戰情室", html);
        var budget = await client.GetAsync("/budget");
        Assert.Contains("你沒有預算權限", await budget.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Invited_github_console_upload_matches()
    {
        var client = _factory.CreateClient();
        var projectId = await SeedInvitedEngineerProjectAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-invited",
            ProjectId = projectId,
            WorkDate = new DateOnly(2026, 9, 1),
            Hours = 8,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
        var uploaded = await res.Content.ReadFromJsonAsync<TimesheetUploadResponse>();
        Assert.NotNull(uploaded);
        Assert.NotEqual(Guid.Empty, uploaded.TimesheetId);
    }

    [Fact]
    public async Task Upload_by_project_code()
    {
        var (personId, _) = await SeedInvitedWithCodeAndRepoAsync("ACME-01", "acme/app");
        _ = personId;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-by-code",
            ProjectCode = "ACME-01",
            WorkDate = new DateOnly(2026, 9, 2),
            Hours = 4,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
    }

    [Fact]
    public async Task Upload_by_repo_slug()
    {
        await SeedInvitedWithCodeAndRepoAsync("ACME-02", "acme/billing");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-by-repo",
            Repos = ["acme/billing"],
            WorkDate = new DateOnly(2026, 9, 3),
            Hours = 2,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
    }

    [Fact]
    public async Task Upload_without_project_identity_is_400()
    {
        await SeedInvitedEngineerProjectAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-no-project",
            WorkDate = new DateOnly(2026, 9, 4),
            Hours = 1,
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var error = await res.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal(ErrorCodes.ProjectUnresolved, error?.Code);
    }

    [Fact]
    public async Task Api_key_can_handshake_and_upload()
    {
        var (personId, projectId) = await SeedInvitedWithCodeAndRepoAsync("KEY-01", "acme/key");
        var plaintext = await SeedApiKeyAsync(personId, "ci");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        var me = await client.GetAsync("/api/v1/me");
        var meBody = await me.Content.ReadAsStringAsync();
        Assert.True(me.IsSuccessStatusCode, meBody);
        var dto = await me.Content.ReadFromJsonAsync<MeDto>();
        Assert.True(dto?.Matched);
        Assert.Equal(personId, dto?.PersonId);

        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-api-key",
            ProjectId = projectId,
            WorkDate = new DateOnly(2026, 9, 5),
            Hours = 3,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");
    }

    [Fact]
    public async Task Upload_persists_contribution_types_and_issues()
    {
        var (personId, projectId) = await SeedInvitedWithCodeAndRepoAsync("CONTRIB-01", "acme/contrib");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invited-token");
        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "slot-contrib",
            ProjectId = projectId,
            WorkDate = new DateOnly(2026, 9, 6),
            Hours = 5,
            IssueNumbers = [42, 43],
            ContributionTypes = ["code", "issue"],
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.StatusCode} {body}");

        using var scope = _factory.Services.CreateScope();
        var sheets = scope.ServiceProvider.GetRequiredService<ITimesheetRepository>();
        var stored = (await sheets.ListForProjectAsync(projectId)).Single(t => t.LocalSlotId == "slot-contrib");
        Assert.Equal(personId, stored.PersonId);
        Assert.Equal([42, 43], stored.IssueNumbers);
        Assert.Equal(["code", "issue"], stored.ContributionTypes);
    }

    async Task SeedHrAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var people = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
        var accounts = scope.ServiceProvider.GetRequiredService<IStaffAccountRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        if (await accounts.GetByUserNameAsync("hr1") is not null)
            return;
        var now = DateTimeOffset.UtcNow;
        var person = Person.Create("人資一", EmploymentKind.FullTime, null, now);
        await people.AddAsync(person);
        var created = StaffAccount.Create("hr1", person.Id, PlatformRole.Hr, hasher.Hash("Hr-Pass-2026"), now);
        Assert.True(created.Ok, created.Message);
        await accounts.AddAsync(created.Value!);
        await uow.SaveChangesAsync();
    }

    async Task<Guid> SeedInvitedEngineerProjectAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var people = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
        var invites = scope.ServiceProvider.GetRequiredService<IInvitationRepository>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        var person = await people.GetByGitHubAsync("invited");
        if (person is null)
        {
            person = Person.Create("invited", EmploymentKind.Freelance, null, now);
            person.BindGitHub("invited", _ => null);
            await people.AddAsync(person);
            var invite = Invitation.Create("invited", PlatformRole.Engineer, null, now);
            invite.BindPerson(person.Id);
            await invites.AddAsync(invite);
        }
        var client = Client.Create("客", ClientKind.External, null);
        await clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        await contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "P", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, now);
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();
        return project.Id;
    }

    async Task<(Guid PersonId, Guid ProjectId)> SeedInvitedWithCodeAndRepoAsync(string projectCode, string ownerRepo)
    {
        using var scope = _factory.Services.CreateScope();
        var people = scope.ServiceProvider.GetRequiredService<IPersonRepository>();
        var invites = scope.ServiceProvider.GetRequiredService<IInvitationRepository>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        var person = await people.GetByGitHubAsync("invited");
        if (person is null)
        {
            person = Person.Create("invited", EmploymentKind.Freelance, null, now);
            person.BindGitHub("invited", _ => null);
            await people.AddAsync(person);
            var invite = Invitation.Create("invited", PlatformRole.Engineer, null, now);
            invite.BindPerson(person.Id);
            await invites.AddAsync(invite);
        }
        var client = Client.Create("客-" + projectCode, ClientKind.External, null);
        await clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "約-" + projectCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        await contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "P-" + projectCode, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, now);
        project.SetProjectCode(projectCode);
        project.AddRepo(ownerRepo);
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();
        return (person.Id, project.Id);
    }

    async Task<string> SeedApiKeyAsync(Guid personId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<IReportingApiKeyRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var (key, plaintext) = ReportingApiKey.Issue(personId, name, DateTimeOffset.UtcNow);
        await keys.AddAsync(key);
        await uow.SaveChangesAsync();
        return plaintext;
    }

    [Fact]
    public async Task Cross_tenant_project_returns_403()
    {
        var foreignProjectId = await SeedForeignTenantProjectAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "owner",
            ["password"] = "AiProject-Owner-2026",
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var res = await client.GetAsync($"/api/v1/projects/{foreignProjectId}");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains(ErrorCodes.Forbidden, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreign_tenant_project_hidden_from_list_scope()
    {
        var foreignProjectId = await SeedForeignTenantProjectAsync();
        using var scope = _factory.Services.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var list = await projects.ListAsync();
        Assert.DoesNotContain(list, p => p.Id == foreignProjectId);
    }

    async Task<Guid> SeedForeignTenantProjectAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiProject.Company.Infrastructure.CompanyDbContext>();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var clients = scope.ServiceProvider.GetRequiredService<IClientRepository>();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var now = DateTimeOffset.UtcNow;
        var other = Tenant.Create("Other Co", now);
        if (!await db.Tenants.AnyAsync(t => t.Id == other.Id))
            db.Tenants.Add(other);
        await db.SaveChangesAsync();
        tenantCtx.Assign(other.Id);
        var client = Client.Create("他司客戶", ClientKind.External, null);
        await clients.AddAsync(client);
        var contract = Contract.Create(client.Id, "他司約", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 1, "TWD", PricingKind.FixedPrice, []);
        await contracts.AddAsync(contract);
        var project = Project.Create(contract.Id, "他司專案", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), RevenueMethod.Milestone, now);
        await projects.AddAsync(project);
        await uow.SaveChangesAsync();
        return project.Id;
    }

    [Fact]
    public async Task Owner_company_account_can_sign_in()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var res = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "owner",
            ["password"] = "AiProject-Owner-2026",
        }));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var page = await client.GetAsync("/accounts");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.Contains("開公司帳戶", html);
        Assert.Contains("邀請 GitHub", html);
    }

    [Fact]
    public async Task Owner_wrong_password_is_rejected()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "owner",
            ["password"] = "wrong-password",
        }));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
