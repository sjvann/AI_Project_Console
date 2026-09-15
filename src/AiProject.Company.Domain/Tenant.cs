namespace AiProject.Company.Domain;

/// <summary>租戶識別常數（A0：單租戶預設；A1 起查詢強制帶 TenantId）。</summary>
public static class TenantIds
{
    /// <summary>開發／單租戶預設工作區。穩定 GUID，可被遷移與種子引用。</summary>
    public static Guid Default { get; } = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
}

/// <summary>租戶列（一需求公司一工作區，D-13／AD-13）。</summary>
public sealed class Tenant
{
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    public static Tenant CreateDefault(DateTimeOffset now) =>
        new()
        {
            Id = TenantIds.Default,
            DisplayName = "Default",
            CreatedAt = now,
        };

    public static Tenant Create(string displayName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException(ErrorCodes.Required, Messages.Required("租戶名稱"));
        return new Tenant
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName.Trim(),
            CreatedAt = now,
        };
    }
}

/// <summary>根聚合需帶租戶邊界。</summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
    void BindTenant(Guid tenantId);
}

/// <summary>目前請求的租戶（A1）。未 Assign 時為 <see cref="TenantIds.Default"/>。</summary>
public interface ITenantContext
{
    Guid TenantId { get; }
    bool IsAssigned { get; }
    void Assign(Guid tenantId);
}

public static class TenantGuards
{
    public static void EnsureBound(ITenantScoped entity, Guid tenantId)
    {
        if (entity.TenantId != tenantId)
            throw new DomainException(ErrorCodes.Forbidden, Messages.Forbidden);
    }

    public static void Bind(ITenantScoped entity, Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new DomainException(ErrorCodes.Required, Messages.Required("租戶"));
        entity.BindTenant(tenantId);
    }
}
