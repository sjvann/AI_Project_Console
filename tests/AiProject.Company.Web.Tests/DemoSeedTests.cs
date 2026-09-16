using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AiProject.Company.Application;
using AiProject.Company.Contracts;
using AiProject.Company.Domain;
using AiProject.Company.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiProject.Company.Web.Tests;

public sealed class DemoSeedApiFactory : WebApplicationFactory<Program>
{
    readonly string _db = Path.Combine(Path.GetTempPath(), "company-demo-" + Guid.NewGuid().ToString("N") + ".db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Company", "Data Source=" + _db);
        builder.UseSetting("Company:EncryptionKey", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        builder.UseSetting("Company:Auth:LocalOwner:Enabled", "true");
        builder.UseSetting("Company:Auth:LocalOwner:UserName", "owner");
        builder.UseSetting("Company:Auth:LocalOwner:Password", "AiProject-Owner-2026");
        builder.UseSetting("Company:Auth:LocalOwner:DisplayName", "公司管理員");
        builder.UseSetting("Company:SeedDemoData", "true");
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
                ["Company:SeedDemoData"] = "true",
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

public class DemoSeedTests : IClassFixture<DemoSeedApiFactory>
{
    readonly DemoSeedApiFactory _factory;
    public DemoSeedTests(DemoSeedApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_page_lists_demo_accounts()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/login");
        Assert.Contains("Demo-Pass-2026", html);
        Assert.Contains("exec", html);
        Assert.Contains("wang-dev", html);
    }

    [Fact]
    public async Task Exec_opens_war_room_with_overdue_project()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "exec",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await client.GetAsync("/war-room");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.False(html.Contains("戰情室載入失敗", StringComparison.Ordinal), html);
        Assert.DoesNotContain("你看不到戰情室", html);
        Assert.Contains("戰情室", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vendor_cannot_open_war_room()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "vendor",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.True(login.IsSuccessStatusCode || login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        var page = await client.GetAsync("/war-room");
        Assert.Contains("你看不到戰情室", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        using var scope = _factory.Services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        await CompanyHostStartup.InitializeAsync(_factory.Services, config);
        var clients = await scope.ServiceProvider.GetRequiredService<IClientRepository>().ListAsync();
        Assert.Equal(1, clients.Count(c => c.Name == CompanyDemoSeed.MarkerClientName));
        var accounts = await scope.ServiceProvider.GetRequiredService<IStaffAccountRepository>().ListAsync();
        Assert.Equal(1, accounts.Count(a => a.UserName == "exec"));
        var invites = await scope.ServiceProvider.GetRequiredService<IInvitationRepository>().ListAsync();
        Assert.Contains(invites, i => i.GitHubLogin == "li-analyst" && i.AcceptedAt is not null);
        Assert.Contains(invites, i => i.GitHubLogin == "newhire-dev" && i.AcceptedAt is null);
        var unmatched = await scope.ServiceProvider.GetRequiredService<IUnmatchedUploadRepository>().ListOpenAsync();
        Assert.Contains(unmatched, u => u.GitHubLogin == "ghost-coder");
        var projects = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().ListAsync();
        Assert.Contains(projects, p => p.Name.Contains("晨星", StringComparison.Ordinal));
        Assert.Contains(projects, p => p.Name == CompanyDemoSeed.SampleProjectName && p.ProjectCode == CompanyDemoSeed.SampleProjectCode);
        Assert.Contains(projects, p => p.TargetEnd < new DateOnly(2026, 9, 6));
        var keys = scope.ServiceProvider.GetRequiredService<IReportingApiKeyRepository>();
        Assert.NotNull(await keys.GetByHashAsync(ReportingApiKey.Hash(CompanyDemoSeed.DemoReportingApiKey)));
        var sheets = await scope.ServiceProvider.GetRequiredService<ITimesheetRepository>().ListAsync();
        Assert.Contains(sheets, s => s.Status == TimesheetStatus.Approved);
        Assert.Contains(sheets, s => s.Status == TimesheetStatus.Returned);
    }

    [Fact]
    public async Task Seed_has_overdue_lead_and_proposal()
    {
        using var scope = _factory.Services.CreateScope();
        var clients = await scope.ServiceProvider.GetRequiredService<IClientRepository>().ListAsync();
        var north = Assert.Single(clients, c => c.Name == "北辰證券");
        Assert.Equal(ClientLifecycle.Lead, north.Lifecycle);
        Assert.Equal(HealthTone.Red, north.FollowUpTone(new DateOnly(2026, 9, 6)));
        Assert.Contains(north.Activities, a => a.Kind == ClientActivityKind.Call);
        Assert.Contains(clients, c => c.Name == "雲端農場" && c.Lifecycle == ClientLifecycle.Proposal);
        Assert.Contains(clients, c => c.Name == CompanyDemoSeed.MarkerClientName && c.Lifecycle == ClientLifecycle.Active);
        var contracts = await scope.ServiceProvider.GetRequiredService<IContractRepository>().ListAsync();
        Assert.DoesNotContain(contracts, c => c.ClientId == north.Id);
        var projects = await scope.ServiceProvider.GetRequiredService<IProjectRepository>().ListAsync();
        Assert.Contains(projects, p => p.Journals.Any(j => j.Kind == ProjectJournalKind.Established));
    }

    [Fact]
    public async Task Pm_clients_page_lists_lead()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "pm",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await client.GetAsync("/clients");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.Contains("客戶", html, StringComparison.Ordinal);
        Assert.Contains("潛在", html, StringComparison.Ordinal);
        Assert.Contains("同一份主檔", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vendor_cannot_see_client_names()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "vendor",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.True(login.IsSuccessStatusCode || login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        var page = await client.GetAsync("/clients");
        var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain("晨星銀行", html);
        Assert.DoesNotContain("北辰證券", html);
        Assert.DoesNotContain("4,800,000", html);
        Assert.DoesNotContain("4800000", html);
    }

    [Fact]
    public async Task Project_page_docs_catalog_is_empty_state_not_500()
    {
        using var scope = _factory.Services.CreateScope();
        var project = (await scope.ServiceProvider.GetRequiredService<IProjectRepository>().ListAsync())
            .First(p => p.Name.Contains("晨星", StringComparison.Ordinal));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "pm",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await client.GetAsync("/projects/" + project.Id);
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.Contains("需求分析", html, StringComparison.Ordinal);
        Assert.Contains("先掛倉或連工作區", html, StringComparison.Ordinal);
        Assert.Contains("專案紀錄", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_api_key_handshakes_and_uploads_to_sample_project()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CompanyDemoSeed.DemoReportingApiKey);
        var me = await client.GetFromJsonAsync<MeDto>("/api/v1/me");
        Assert.NotNull(me);
        Assert.True(me.Matched);
        Assert.Equal(CompanyDemoSeed.DemoEngineerGitHub, me.GitHubLogin);

        var res = await client.PostAsJsonAsync("/api/v1/timesheets/upload", new TimesheetUploadRequest
        {
            LocalSlotId = "from-console-sample",
            ProjectCode = CompanyDemoSeed.SampleProjectCode,
            WorkDate = new DateOnly(2026, 9, 16),
            Hours = 1.5m,
        });
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, body);
        Assert.Contains("pending_pm", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pm_opens_reporting_inbox()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "pm",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await client.GetAsync("/reporting");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        Assert.Contains("公開回報", html, StringComparison.Ordinal);
        Assert.Contains("待 PM 確認", html, StringComparison.Ordinal);
        Assert.Contains("/api/v1/timesheets/upload", html, StringComparison.Ordinal);
        Assert.Contains(CompanyDemoSeed.SampleProjectName, html, StringComparison.Ordinal);
        Assert.Contains(CompanyDemoSeed.DemoReportingApiKey, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vendor_cannot_open_reporting_inbox()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsync("/login/account", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "vendor",
            ["password"] = CompanyDemoSeed.SharedPassword,
        }));
        Assert.True(login.IsSuccessStatusCode || login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        var page = await client.GetAsync("/reporting");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("你沒有查看公開回報入帳的權限", html, StringComparison.Ordinal);
        Assert.DoesNotContain(CompanyDemoSeed.DemoReportingApiKey, html);
    }
}
