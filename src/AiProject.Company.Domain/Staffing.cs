namespace AiProject.Company.Domain;

public sealed class ContractStaffingPolicy : IContractStaffingPolicy
{
    public Outcome CanAssign(Person person, Project project, Contract contract)
    {
        if (person.EmploymentKind == EmploymentKind.FullTime)
            return Outcome.Success();
        if (person.VendorId is Guid vendorId)
            return contract.AllowsVendor(vendorId)
                ? Outcome.Success()
                : Outcome.Fail(ErrorCodes.VendorNotAuthorized, Messages.VendorNotAuthorized);
        if (contract.AuthorizedVendorIds.Count == 0 || contract.AuthorizedVendorIds.Contains(person.Id))
            return Outcome.Success();
        return Outcome.Fail(ErrorCodes.VendorNotAuthorized, Messages.VendorNotAuthorized);
    }
}
