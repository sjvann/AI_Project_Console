using AiProject.Company.Application;
using AiProject.Company.Domain;

namespace AiProject.Company.Application.Tests;

sealed class StubUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;
    public Guid? PersonId { get; set; }
    public string UserName { get; set; } = "alice";
    public string GitHubLogin { get; set; } = "alice";
    public string DisplayName { get; set; } = "Alice";
    public PlatformRole Role { get; set; }
    public Guid? VendorId { get; set; }
    public IReadOnlySet<Guid> AuthorizedProjectIds { get; set; } = new HashSet<Guid>();
}

public class AuthorizationTests
{
    [Fact]
    public void Engineer_cannot_see_war_room_or_client_rate()
    {
        var gate = new AuthorizationGate(new StubUser { Role = PlatformRole.Engineer, PersonId = Guid.NewGuid() });
        Assert.False(gate.Can(PlatformCapability.ViewWarRoom));
        Assert.Equal(ErrorCodes.RateHidden, gate.Ensure(PlatformCapability.ViewClientRate).Code);
        Assert.Equal(Messages.RateHidden, gate.Ensure(PlatformCapability.ViewSalary).Message);
    }

    [Fact]
    public void Vendor_admin_cannot_see_other_vendor_or_margin()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var gate = new AuthorizationGate(new StubUser { Role = PlatformRole.VendorAdmin, PersonId = Guid.NewGuid(), VendorId = mine });
        Assert.False(gate.Can(PlatformCapability.ViewWarRoom));
        Assert.False(gate.Can(PlatformCapability.ManagePeople, vendorId: other));
        Assert.True(gate.Can(PlatformCapability.SubmitVendorTimesheet, vendorId: mine));
        Assert.False(gate.Can(PlatformCapability.ManageBudget));
        Assert.False(gate.Can(PlatformCapability.ManageClients));
    }

    [Fact]
    public void Exec_lands_on_war_room_pm_on_projects()
    {
        Assert.Equal("/war-room", new HomeRouteResolver().PathFor(PlatformRole.Exec));
        Assert.Equal("/projects", new HomeRouteResolver().PathFor(PlatformRole.Pm));
    }

    [Fact]
    public void Unauthenticated_is_rejected()
    {
        var gate = new AuthorizationGate(new StubUser { IsAuthenticated = false });
        Assert.Equal(ErrorCodes.Unauthenticated, gate.Ensure(PlatformCapability.Dispatch).Code);
    }

    [Fact]
    public void Delivery_can_force_overload_pm_cannot()
    {
        Assert.True(new AuthorizationGate(new StubUser { Role = PlatformRole.Delivery, PersonId = Guid.NewGuid() }).Can(PlatformCapability.ForceOverload));
        Assert.False(new AuthorizationGate(new StubUser { Role = PlatformRole.Pm, PersonId = Guid.NewGuid() }).Can(PlatformCapability.ForceOverload));
    }

    [Fact]
    public void Owner_without_person_can_manage_settings()
    {
        var gate = new AuthorizationGate(new StubUser { Role = PlatformRole.Owner, PersonId = null });
        Assert.True(gate.Can(PlatformCapability.ManageSettings));
        Assert.True(gate.Can(PlatformCapability.InviteUser));
        Assert.True(gate.Can(PlatformCapability.ViewWarRoom));
    }
}
