namespace AiProject.Console.Core.Agents;

/// <summary>
/// 專案問答一定會送 tools；清單與測試只保留能 function calling 的聊天模型。
/// </summary>
public static class ProjectAskModelSupport
{
    public const string NeedToolsHint = "專案問答會呼叫本機工具，請改選支援 function calling 的模型（例如 llama3.2、qwen2.5）。";

    /// <summary>已知在 Ollama／常見本機端點不能帶 tools 的模型族。</summary>
    static readonly string[] NoToolsFamilies =
    [
        "gemma", "codegemma",
        "phi", "phi2", "phi3",
        "tinyllama", "tinydolphin", "orca-mini",
        "moondream", "llava", "bakllava", "minicpm-v",
        "llama2",
        "starcoder", "starcoder2", "stable-code", "stablelm", "stablelm2",
        "deepseek-r1",
        "nomic-embed-text", "mxbai-embed-large", "all-minilm", "snowflake-arctic-embed",
        "bge-m3", "bge-large", "bge-base",
    ];

    static readonly string[] NonChatTokens =
    [
        "embed", "embedding", "whisper", "tts", "dall-e", "dalle",
        "moderation", "gpt-image", "text-embedding",
    ];

    public static bool SupportsTools(string? model)
    {
        var family = Family(model);
        if (string.IsNullOrEmpty(family))
            return false;
        foreach (var token in NonChatTokens)
        {
            if (family.Contains(token, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var prefix in NoToolsFamilies)
        {
            if (FamilyMatches(family, prefix))
                return false;
        }

        return true;
    }

    public static IReadOnlyList<string> FilterToolCapable(IEnumerable<string> models)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var m in models)
        {
            var t = (m ?? "").Trim();
            if (string.IsNullOrEmpty(t) || !SupportsTools(t) || !seen.Add(t))
                continue;
            list.Add(t);
        }
        return list;
    }

    public static string? RejectReason(string? model)
    {
        var t = (model ?? "").Trim();
        if (string.IsNullOrEmpty(t) || SupportsTools(t))
            return null;
        var family = Family(t);
        if (family.Contains("embed", StringComparison.OrdinalIgnoreCase))
            return t + " 是嵌入模型，不能當問答助手。";
        return t + " 不支援工具呼叫。" + NeedToolsHint;
    }

    public static bool LooksLikeToolsUnsupportedError(string? raw) =>
        (raw ?? "").Contains("does not support tools", StringComparison.OrdinalIgnoreCase);

    public static string Family(string? model)
    {
        var t = (model ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return "";
        var slash = t.LastIndexOf('/');
        if (slash >= 0 && slash < t.Length - 1)
            t = t[(slash + 1)..];
        var colon = t.IndexOf(':');
        if (colon > 0)
            t = t[..colon];
        return t.Trim();
    }

    static bool FamilyMatches(string family, string prefix)
    {
        if (family.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!family.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        if (family.Length == prefix.Length)
            return true;
        var next = family[prefix.Length];
        return !char.IsLetter(next);
    }
}
