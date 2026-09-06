using AiProject.Company.Domain;

namespace AiProject.Company.Application;

public sealed class AuthorizationGate : IAuthorizationGate
{
    readonly ICurrentUser _user;

    public AuthorizationGate(ICurrentUser user) => _user = user;

    public bool Can(PlatformCapability capability, Guid? projectId = null, Guid? personId = null, Guid? vendorId = null) =>
        Ensure(capability, projectId, personId, vendorId).Ok;

    public Outcome Ensure(PlatformCapability capability, Guid? projectId = null, Guid? personId = null, Guid? vendorId = null)
    {
        if (!_user.IsAuthenticated)
            return Outcome.Fail(ErrorCodes.Unauthenticated, Messages.Unauthenticated);

        var role = _user.Role;
        if (role == PlatformRole.Owner)
            return Outcome.Success();
        if (_user.PersonId is null && capability is not PlatformCapability.UploadTimesheet)
            return Outcome.Fail(ErrorCodes.NotInvited, Messages.NotInvited);

        if (capability == PlatformCapability.ViewWarRoom)
            return role is PlatformRole.Exec or PlatformRole.Delivery or PlatformRole.Finance
                ? Outcome.Success()
                : Outcome.Fail(ErrorCodes.Forbidden, Messages.Forbidden);

        if (capability == PlatformCapability.ManageSettings)
            return Deny();

        if (capability == PlatformCapability.InviteUser || capability == PlatformCapability.ManagePeople)
            return role == PlatformRole.Hr || (role == PlatformRole.VendorAdmin && MatchesVendor(vendorId))
                ? Outcome.Success()
                : Deny();

        if (capability == PlatformCapability.ViewSalary)
        {
            if (role is PlatformRole.Hr or PlatformRole.Finance)
                return Outcome.Success();
            if (capability == PlatformCapability.ViewSalary && personId is Guid pid && pid == _user.PersonId && role is PlatformRole.Engineer or PlatformRole.VendorEngineer or PlatformRole.VendorAdmin)
                return Outcome.Success();
            return Outcome.Fail(ErrorCodes.RateHidden, Messages.RateHidden);
        }

        if (capability == PlatformCapability.ChangeRate)
            return role is PlatformRole.Hr ? Outcome.Success() : Deny();

        if (capability == PlatformCapability.LockPayroll)
            return role is PlatformRole.Hr ? Outcome.Success() : Deny();

        if (capability == PlatformCapability.ForceOverload)
            return role is PlatformRole.Delivery ? Outcome.Success() : Deny();

        if (capability == PlatformCapability.Dispatch)
        {
            if (role is PlatformRole.Delivery)
                return Outcome.Success();
            if (role is PlatformRole.Pm && ProjectOk(projectId))
                return Outcome.Success();
            return Deny();
        }

        if (capability == PlatformCapability.ConfirmTimesheet)
            return role is PlatformRole.Pm or PlatformRole.Delivery or PlatformRole.Hr
                && ProjectOk(projectId)
                ? Outcome.Success()
                : Deny();

        if (capability == PlatformCapability.ManageBudget || capability == PlatformCapability.RecognizeRevenue)
            return role is PlatformRole.Finance or PlatformRole.Pm or PlatformRole.Delivery
                && ProjectOk(projectId)
                ? Outcome.Success()
                : Deny();

        if (capability == PlatformCapability.ViewClientRate)
        {
            if (role is PlatformRole.Finance or PlatformRole.Delivery)
                return Outcome.Success();
            if (role is PlatformRole.Pm && ProjectOk(projectId))
                return Outcome.Success();
            return Outcome.Fail(ErrorCodes.RateHidden, Messages.RateHidden);
        }

        if (capability == PlatformCapability.UploadTimesheet)
        {
            if (role is PlatformRole.Engineer or PlatformRole.Lead or PlatformRole.VendorEngineer)
                return personId is null || personId == _user.PersonId || _user.PersonId is null
                    ? Outcome.Success()
                    : Deny();
            return Deny();
        }

        if (capability == PlatformCapability.SubmitVendorTimesheet)
            return role == PlatformRole.VendorAdmin && MatchesVendor(vendorId) ? Outcome.Success() : Deny();

        if (capability == PlatformCapability.ViewOwnPayslip)
            return personId is null || personId == _user.PersonId ? Outcome.Success() : Deny();

        if (capability == PlatformCapability.ManageProjects || capability == PlatformCapability.ManageClients)
        {
            if (role is PlatformRole.Delivery or PlatformRole.Finance)
                return Outcome.Success();
            if (role is PlatformRole.Pm && ProjectOk(projectId))
                return Outcome.Success();
            return Deny();
        }

        return Deny();
    }

    bool ProjectOk(Guid? projectId)
    {
        if (_user.Role is PlatformRole.Delivery or PlatformRole.Exec or PlatformRole.Hr or PlatformRole.Finance or PlatformRole.Owner)
            return true;
        if (projectId is null)
            return true;
        return _user.AuthorizedProjectIds.Contains(projectId.Value);
    }

    bool MatchesVendor(Guid? vendorId) =>
        vendorId is Guid id && _user.VendorId == id;

    static Outcome Deny() => Outcome.Fail(ErrorCodes.Forbidden, Messages.Forbidden);
}
