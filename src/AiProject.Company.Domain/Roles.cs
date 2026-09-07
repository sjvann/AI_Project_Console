namespace AiProject.Company.Domain;

public enum PlatformRole
{
    Owner = 0,
    Exec = 1,
    Delivery = 2,
    Pm = 3,
    Lead = 4,
    Engineer = 5,
    VendorAdmin = 6,
    VendorEngineer = 7,
    Hr = 8,
    Finance = 9,
}

public enum PlatformCapability
{
    ViewWarRoom,
    ManageSettings,
    ManagePeople,
    InviteUser,
    ViewSalary,
    ChangeRate,
    LockPayroll,
    ForceOverload,
    Dispatch,
    ConfirmTimesheet,
    ManageBudget,
    RecognizeRevenue,
    ViewClientRate,
    UploadTimesheet,
    SubmitVendorTimesheet,
    ViewOwnPayslip,
    ManageProjects,
    ManageClients,
}

public static class PlatformRoles
{
    public static string Code(this PlatformRole role) => role switch
    {
        PlatformRole.Owner => "owner",
        PlatformRole.Exec => "exec",
        PlatformRole.Delivery => "delivery",
        PlatformRole.Pm => "pm",
        PlatformRole.Lead => "lead",
        PlatformRole.Engineer => "engineer",
        PlatformRole.VendorAdmin => "vendor_admin",
        PlatformRole.VendorEngineer => "vendor_engineer",
        PlatformRole.Hr => "hr",
        PlatformRole.Finance => "finance",
        _ => "engineer",
    };

    public static string Display(this PlatformRole role) => role switch
    {
        PlatformRole.Owner => "公司管理員",
        PlatformRole.Exec => "經營層",
        PlatformRole.Delivery => "交付主管",
        PlatformRole.Pm => "專案經理",
        PlatformRole.Lead => "Tech Lead",
        PlatformRole.Engineer => "工程師",
        PlatformRole.VendorAdmin => "外包窗口",
        PlatformRole.VendorEngineer => "外包工程師",
        PlatformRole.Hr => "人資",
        PlatformRole.Finance => "財務",
        _ => role.ToString(),
    };

    public static bool TryParse(string? value, out PlatformRole role)
    {
        role = value?.Trim().ToLowerInvariant() switch
        {
            "owner" => PlatformRole.Owner,
            "exec" => PlatformRole.Exec,
            "delivery" => PlatformRole.Delivery,
            "pm" => PlatformRole.Pm,
            "lead" => PlatformRole.Lead,
            "engineer" => PlatformRole.Engineer,
            "vendor_admin" => PlatformRole.VendorAdmin,
            "vendor_engineer" => PlatformRole.VendorEngineer,
            "hr" => PlatformRole.Hr,
            "finance" => PlatformRole.Finance,
            _ => (PlatformRole)(-1),
        };
        return (int)role >= 0;
    }

    public static bool IsVendor(this PlatformRole role) =>
        role is PlatformRole.VendorAdmin or PlatformRole.VendorEngineer;

    public static bool UsesGitHubSignIn(this PlatformRole role) =>
        role is PlatformRole.Engineer or PlatformRole.Lead or PlatformRole.VendorEngineer;

    public static bool UsesCompanyAccount(this PlatformRole role) =>
        !role.UsesGitHubSignIn();

    public static IReadOnlyList<PlatformRole> GitHubSignInRoles { get; } =
        [PlatformRole.Engineer, PlatformRole.Lead, PlatformRole.VendorEngineer];

    public static IReadOnlyList<PlatformRole> CompanyAccountRoles { get; } =
        [PlatformRole.Owner, PlatformRole.Exec, PlatformRole.Delivery, PlatformRole.Pm, PlatformRole.Hr, PlatformRole.Finance, PlatformRole.VendorAdmin];

    public static bool LandsOnWarRoom(this PlatformRole role) =>
        role is PlatformRole.Exec or PlatformRole.Delivery;

    public static bool LandsOnProjects(this PlatformRole role) =>
        role is PlatformRole.Pm or PlatformRole.Lead;

    public static bool LandsOnPeople(this PlatformRole role) =>
        role is PlatformRole.Hr or PlatformRole.Finance or PlatformRole.Owner;

    public static bool LandsOnMyHours(this PlatformRole role) =>
        role is PlatformRole.Engineer or PlatformRole.VendorAdmin or PlatformRole.VendorEngineer;
}
