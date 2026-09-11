using System.Security.Cryptography;
using System.Text;
using AiProject.Company.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AiProject.Company.Infrastructure;

public sealed class AesFieldEncryptor : IFieldEncryptor
{
    readonly byte[] _key;

    public AesFieldEncryptor(IConfiguration configuration)
    {
        var raw = configuration["Company:EncryptionKey"] ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            throw new DomainException(ErrorCodes.InvalidState, "尚未設定加密金鑰 Company:EncryptionKey，薪資欄無法讀寫。請先備份並填入 32 位元組 Base64 金鑰。");
        try
        {
            _key = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        }
        if (_key.Length != 32)
            _key = SHA256.HashData(_key);
    }

    public string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, bytes, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string Decrypt(string ciphertext)
    {
        var data = Convert.FromBase64String(ciphertext);
        if (data.Length < 28)
            throw new DomainException(ErrorCodes.InvalidState, "薪資密文損壞，請用備份金鑰還原。");
        var nonce = data[..12];
        var tag = data[12..28];
        var cipher = data[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly Today(string timeZoneId)
    {
        var tz = Resolve(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(local);
    }

    static TimeZoneInfo Resolve(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(timeZoneId, "Asia/Taipei", StringComparison.OrdinalIgnoreCase))
                return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
            return TimeZoneInfo.Utc;
        }
    }
}

public sealed class AspNetPasswordHasher : IPasswordHasher
{
    readonly PasswordHasher<object> _hasher = new();
    public string Hash(string password) => _hasher.HashPassword(null!, password);
    public bool Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(null!, hash, password) is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}

public sealed class HttpCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; init; }
    public Guid? PersonId { get; init; }
    public string UserName { get; init; } = "";
    public string GitHubLogin { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public PlatformRole Role { get; init; } = PlatformRole.Engineer;
    public Guid? VendorId { get; init; }
    public IReadOnlySet<Guid> AuthorizedProjectIds { get; init; } = new HashSet<Guid>();
}

public sealed class WorkspaceIntakeReader : Application.IWorkspaceIntakeReader
{
    public async Task<IntakeSummary?> ReadAsync(string workspacePath, CancellationToken ct = default)
    {
        var path = Path.Combine(workspacePath, "docs", "product", "intake.json");
        if (!File.Exists(path))
            return null;
        var json = await File.ReadAllTextAsync(path, ct);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var issued = 0;
        var pending = 0;
        if (doc.RootElement.TryGetProperty("intakes", out var intakes) && intakes.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in intakes.EnumerateArray())
            {
                var stage = item.TryGetProperty("stage", out var s) ? s.GetString() : "";
                if (string.Equals(stage, "issued", StringComparison.OrdinalIgnoreCase) || string.Equals(stage, "已發出", StringComparison.OrdinalIgnoreCase))
                    issued++;
                else if (string.Equals(stage, "acceptance", StringComparison.OrdinalIgnoreCase) || string.Equals(stage, "待驗收", StringComparison.OrdinalIgnoreCase))
                    pending++;
            }
        }
        return new IntakeSummary(issued, pending);
    }
}
