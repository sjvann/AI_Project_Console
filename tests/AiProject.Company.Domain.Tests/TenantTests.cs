using AiProject.Company.Domain;

namespace AiProject.Company.Domain.Tests;

public class TenantTests
{
    [Fact]
    public void Default_tenant_id_is_stable()
    {
        Assert.Equal(Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001"), TenantIds.Default);
    }

    [Fact]
    public void New_aggregates_bind_default_tenant()
    {
        var now = DateTimeOffset.UtcNow;
        var person = Person.Create("測試", EmploymentKind.FullTime, null, now);
        Assert.Equal(TenantIds.Default, person.TenantId);

        var vendor = Vendor.Create("外包", null, new DateOnly(2026, 1, 1), null);
        Assert.Equal(TenantIds.Default, vendor.TenantId);

        var client = Client.Create("客戶", ClientKind.External, null);
        Assert.Equal(TenantIds.Default, client.TenantId);

        var settings = CompanySettings.CreateDefault();
        Assert.Equal(TenantIds.Default, settings.TenantId);

        var tenant = Tenant.CreateDefault(now);
        Assert.Equal(TenantIds.Default, tenant.Id);
        Assert.Equal("Default", tenant.DisplayName);
    }
}
