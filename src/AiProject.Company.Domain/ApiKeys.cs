using System.Security.Cryptography;
using System.Text;

namespace AiProject.Company.Domain;

/// <summary>公司核發的公開回報 API 金鑰（對應人員；只存雜湊）。</summary>
public sealed class ReportingApiKey : ITenantScoped
{
    public const string Prefix = "apk_";

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; } = TenantIds.Default;
    public void BindTenant(Guid tenantId) => TenantId = tenantId == Guid.Empty ? throw new DomainException(ErrorCodes.Required, Messages.Required("租戶")) : tenantId;
    public Guid PersonId { get; private set; }
    public string Name { get; private set; } = "";
    /// <summary>顯示用前綴（含 apk_），不可還原完整金鑰。</summary>
    public string KeyPrefix { get; private set; } = "";
    public string KeyHash { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static (ReportingApiKey Key, string Plaintext) Issue(Guid personId, string name, DateTimeOffset now)
    {
        var plaintext = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        return (FromPlaintext(personId, name, plaintext, now), plaintext);
    }

    /// <summary>用已知明文建金鑰（示範種子）；雜湊規則與核發相同。</summary>
    public static ReportingApiKey FromPlaintext(Guid personId, string name, string plaintext, DateTimeOffset now)
    {
        if (personId == Guid.Empty)
            throw new DomainException(ErrorCodes.Required, Messages.Required("人員"));
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException(ErrorCodes.Required, Messages.Required("金鑰名稱"));
        if (!LooksLikeApiKey(plaintext))
            throw new DomainException(ErrorCodes.InvalidState, "回報 API 金鑰必須以 apk_ 開頭。");
        var secret = plaintext.Trim();
        return new ReportingApiKey
        {
            Id = Guid.NewGuid(),
            PersonId = personId,
            Name = name.Trim(),
            KeyPrefix = secret.Length <= 12 ? secret : secret[..12],
            KeyHash = Hash(secret),
            CreatedAt = now,
        };
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;
        RevokedAt = now;
    }

    public void Touch(DateTimeOffset now) => LastUsedAt = now;

    public static string Hash(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new DomainException(ErrorCodes.Required, Messages.Required("API 金鑰"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext.Trim()))).ToLowerInvariant();
    }

    public static bool LooksLikeApiKey(string token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Trim().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
}
