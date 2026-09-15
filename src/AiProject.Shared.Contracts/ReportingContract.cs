namespace AiProject.Shared.Contracts;

/// <summary>公開回報契約共用常數與白名單規則（AD-17／AD-18）。</summary>
public static class ReportingContract
{
    public const string ApiVersionHeader = "X-Company-Api-Version";
    public const string CurrentVersion = "1";

    /// <summary>extension／extra 鍵名若含這些片段則拒絕（path／blob／source／file／code）。</summary>
    public static bool IsForbiddenExtensionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;
        return key.Contains("path", StringComparison.OrdinalIgnoreCase)
            || key.Contains("blob", StringComparison.OrdinalIgnoreCase)
            || key.Contains("source", StringComparison.OrdinalIgnoreCase)
            || key.Contains("file", StringComparison.OrdinalIgnoreCase)
            || key.Contains("code", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasForbiddenExtensionKeys(IEnumerable<string>? keys)
    {
        if (keys is null)
            return false;
        foreach (var key in keys)
        {
            if (IsForbiddenExtensionKey(key))
                return true;
        }
        return false;
    }
}

public sealed class ContractError
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}
