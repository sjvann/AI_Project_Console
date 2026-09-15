namespace AiProject.Company.Domain;

public enum EmploymentKind
{
    FullTime = 0,
    Freelance = 1,
    VendorStaff = 2,
}

public enum PersonStatus
{
    Active = 0,
    Inactive = 1,
    Blacklisted = 2,
}

public static class PersonLabels
{
    public static string Display(this EmploymentKind kind) => kind switch
    {
        EmploymentKind.FullTime => "正職",
        EmploymentKind.Freelance => "個人外包",
        EmploymentKind.VendorStaff => "承攬派駐",
        _ => kind.ToString(),
    };

    public static string Display(this PersonStatus status) => status switch
    {
        PersonStatus.Active => "在職",
        PersonStatus.Inactive => "停用",
        PersonStatus.Blacklisted => "黑名單",
        _ => status.ToString(),
    };
}

public sealed class Person : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public string DisplayName { get; private set; } = "";
    public EmploymentKind EmploymentKind { get; private set; }
    public PersonStatus Status { get; private set; }
    public string? GitHubLogin { get; private set; }
    public string? Email { get; private set; }
    public List<string> Skills { get; private set; } = [];
    public decimal WeeklyHourCap { get; private set; } = 40;
    public string? Department { get; private set; }
    public Guid? ManagerPersonId { get; private set; }
    public Guid? VendorId { get; private set; }
    public string? MonthlySalaryCipher { get; private set; }
    public string? InternalHourlyRateCipher { get; private set; }
    public bool CostAllocationExempt { get; private set; }
    public List<UnavailableRange> Unavailable { get; private set; } = [];
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string? GitHubKey => string.IsNullOrWhiteSpace(GitHubLogin) ? null : "github:" + GitHubLogin.Trim().ToLowerInvariant();

    public static Person Create(string displayName, EmploymentKind kind, Guid? vendorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException(ErrorCodes.Required, Messages.Required("顯示名"));
        if (kind == EmploymentKind.VendorStaff && vendorId is null)
            throw new DomainException(ErrorCodes.Required, Messages.Required("所屬外包商"));
        if (kind != EmploymentKind.VendorStaff)
            vendorId = null;
        return new Person
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName.Trim(),
            EmploymentKind = kind,
            Status = PersonStatus.Active,
            VendorId = vendorId,
            CreatedAt = now,
        };
    }

    public void UpdateProfile(string displayName, string? email, string? department, Guid? managerPersonId, decimal weeklyHourCap, IEnumerable<string> skills)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException(ErrorCodes.Required, Messages.Required("顯示名"));
        if (weeklyHourCap <= 0)
            throw new DomainException(ErrorCodes.InvalidState, "每週工時上限必須大於 0。");
        DisplayName = displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        Department = string.IsNullOrWhiteSpace(department) ? null : department.Trim();
        ManagerPersonId = managerPersonId;
        WeeklyHourCap = weeklyHourCap;
        Skills = skills.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList();
    }

    public Outcome BindGitHub(string login, Func<string, Guid?> otherActivePerson)
    {
        if (string.IsNullOrWhiteSpace(login))
            return Outcome.Fail(ErrorCodes.Required, Messages.Required("GitHub 帳號"));
        var normalized = login.Trim().ToLowerInvariant();
        var other = otherActivePerson(normalized);
        if (other is Guid id && id != Id)
            return Outcome.Fail(ErrorCodes.GithubBound, Messages.GithubBound);
        GitHubLogin = normalized;
        return Outcome.Success();
    }

    public void SetStatus(PersonStatus status) => Status = status;

    public void SetCostAllocationExempt(bool exempt) => CostAllocationExempt = exempt;

    public void SetCompensationCiphers(string? monthlySalaryCipher, string? internalHourlyRateCipher)
    {
        MonthlySalaryCipher = monthlySalaryCipher;
        InternalHourlyRateCipher = internalHourlyRateCipher;
    }

    public void ReplaceUnavailable(IEnumerable<UnavailableRange> ranges) =>
        Unavailable = ranges.ToList();

    public Outcome EnsureCanAssign()
    {
        if (IsDeleted || Status != PersonStatus.Active)
            return Outcome.Fail(ErrorCodes.PersonInactive, Messages.PersonInactive);
        return Outcome.Success();
    }

    public Outcome EnsureCanHardDelete(bool hasTimesheetsOrAssignments)
    {
        if (hasTimesheetsOrAssignments)
            return Outcome.Fail(ErrorCodes.SoftDeleteBlocked, Messages.SoftDeleteBlocked);
        return Outcome.Success();
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        Status = PersonStatus.Inactive;
    }

    public bool IsUnavailableOn(DateOnly date) =>
        Unavailable.Any(r => date >= r.Start && date <= r.End);
}

public sealed class UnavailableRange
{
    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public string Reason { get; private set; } = "";

    public UnavailableRange(DateOnly start, DateOnly end, string reason)
    {
        if (end < start)
            throw new DomainException(ErrorCodes.InvalidState, "不可派結束日不能早於開始日。");
        Start = start;
        End = end;
        Reason = string.IsNullOrWhiteSpace(reason) ? "不可派" : reason.Trim();
    }
}

public sealed class Vendor : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public string Name { get; private set; } = "";
    public string? TaxId { get; private set; }
    public Guid? WindowPersonId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public string? DefaultRateCipher { get; private set; }
    public bool IsDeleted { get; private set; }

    public static Vendor Create(string name, string? taxId, DateOnly validFrom, DateOnly? validTo)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("外包商名稱"));
        return new Vendor
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            TaxId = string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim(),
            ValidFrom = validFrom,
            ValidTo = validTo,
        };
    }

    public void Update(string name, string? taxId, Guid? windowPersonId, DateOnly validFrom, DateOnly? validTo, string? defaultRateCipher)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("外包商名稱"));
        Name = name.Trim();
        TaxId = string.IsNullOrWhiteSpace(taxId) ? null : taxId.Trim();
        WindowPersonId = windowPersonId;
        ValidFrom = validFrom;
        ValidTo = validTo;
        DefaultRateCipher = defaultRateCipher;
    }

    public void SoftDelete() => IsDeleted = true;
}

public sealed class Invitation : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public string GitHubLogin { get; private set; } = "";
    public PlatformRole Role { get; private set; }
    public Guid? VendorId { get; private set; }
    public Guid? PersonId { get; private set; }
    public DateTimeOffset InvitedAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public bool Revoked { get; private set; }

    public static Invitation Create(string gitHubLogin, PlatformRole role, Guid? vendorId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(gitHubLogin))
            throw new DomainException(ErrorCodes.Required, Messages.Required("GitHub 帳號"));
        if (role.IsVendor() && vendorId is null)
            throw new DomainException(ErrorCodes.Required, Messages.Required("外包商"));
        if (!role.UsesGitHubSignIn())
            throw new DomainException(ErrorCodes.InvalidState, Messages.CompanyAccountInstead);
        return new Invitation
        {
            Id = Guid.NewGuid(),
            GitHubLogin = gitHubLogin.Trim().ToLowerInvariant(),
            Role = role,
            VendorId = role.IsVendor() ? vendorId : null,
            InvitedAt = now,
        };
    }

    public void BindPerson(Guid personId) => PersonId = personId;

    public void Accept(DateTimeOffset now)
    {
        if (Revoked)
            throw new DomainException(ErrorCodes.InvalidState, "這張邀請已撤銷。");
        AcceptedAt = now;
    }

    public void Revoke() => Revoked = true;
}

public sealed class UnmatchedUpload : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public string GitHubLogin { get; private set; } = "";
    public string LocalSlotId { get; private set; } = "";
    public string PayloadJson { get; private set; } = "";
    public DateTimeOffset ReceivedAt { get; private set; }
    public Guid? BoundPersonId { get; private set; }

    public static UnmatchedUpload Capture(string gitHubLogin, string localSlotId, string payloadJson, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            GitHubLogin = gitHubLogin.Trim().ToLowerInvariant(),
            LocalSlotId = localSlotId,
            PayloadJson = payloadJson,
            ReceivedAt = now,
        };

    public void Bind(Guid personId) => BoundPersonId = personId;
}
