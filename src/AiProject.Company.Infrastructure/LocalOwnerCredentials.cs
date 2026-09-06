using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace AiProject.Company.Infrastructure;

public sealed class LocalOwnerCredentials
{
    readonly PasswordHasher<object> _hasher = new();
    readonly string _hash;

    public LocalOwnerCredentials(IConfiguration configuration)
    {
        var section = configuration.GetSection("Company:Auth:LocalOwner");
        UserName = (section["UserName"] ?? "owner").Trim();
        DisplayName = string.IsNullOrWhiteSpace(section["DisplayName"]) ? "公司管理員" : section["DisplayName"]!.Trim();
        var password = section["Password"] ?? "";
        var configuredHash = section["PasswordHash"] ?? "";
        Enabled = section.GetValue("Enabled", false) && (!string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(configuredHash));
        var bootstrap = configuration["Company:BootstrapOwnerGitHubLogin"];
        PersonLogin = FirstNonEmpty(bootstrap, section["PersonLogin"]);
        if (string.IsNullOrEmpty(PersonLogin) && Enabled)
            PersonLogin = UserName.Trim().ToLowerInvariant();
        _hash = !string.IsNullOrEmpty(configuredHash)
            ? configuredHash
            : Enabled ? _hasher.HashPassword(null!, password) : "";
    }

    public bool Enabled { get; }
    public string UserName { get; }
    public string DisplayName { get; }
    public string PersonLogin { get; }

    public bool Verify(string userName, string password)
    {
        if (!Enabled)
            return false;
        if (string.IsNullOrEmpty(password))
            return false;
        if (!string.Equals(userName.Trim(), UserName, StringComparison.OrdinalIgnoreCase))
            return false;
        var result = _hasher.VerifyHashedPassword(null!, _hash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }

    static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().ToLowerInvariant();
        }
        return "";
    }
}
