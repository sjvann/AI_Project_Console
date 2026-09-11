using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed record ProjectAskProvider(
    string Id,
    string Title,
    string Hint,
    string BaseUrl,
    string DefaultModel,
    bool NeedsApiKey,
    IReadOnlyList<string> SuggestedModels);

public sealed record ProjectAskSource(
    string Id,
    string Title,
    string BaseUrl,
    string Model,
    string? ApiKey = null);

/// <summary>專案問答常見 OpenAI 相容端點；點範本即可帶入網址與模型。</summary>
public static class ProjectAskProviders
{
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<ProjectAskProvider> Catalog =
    [
        new(
            "ollama",
            "本機 Ollama",
            "本機、免 API key。需先啟動 Ollama。建議模型可在下方一鍵 pull。Gemma 只能聊天，不會列入。",
            "http://127.0.0.1:11434/v1",
            "llama3.2",
            false,
            ["llama3.2", "llama3.1", "qwen2.5", "mistral"]),
        new(
            "lmstudio",
            "LM Studio",
            "本機、免 API key。在 LM Studio 開啟本機伺服器。",
            "http://127.0.0.1:1234/v1",
            "",
            false,
            []),
        new(
            "openai",
            "OpenAI",
            "雲端。填官方 API key。",
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            true,
            ["gpt-4o-mini", "gpt-4o", "gpt-4.1-mini"]),
        new(
            "groq",
            "Groq",
            "雲端、速度快。填 Groq API key。",
            "https://api.groq.com/openai/v1",
            "llama-3.1-8b-instant",
            true,
            ["llama-3.1-8b-instant", "llama-3.3-70b-versatile"]),
        new(
            "openrouter",
            "OpenRouter",
            "一個 key 可選多家模型。",
            "https://openrouter.ai/api/v1",
            "openai/gpt-4o-mini",
            true,
            ["openai/gpt-4o-mini", "meta-llama/llama-3.3-70b-instruct"]),
        new(
            CustomId,
            "自訂",
            "任何 OpenAI 相容 /v1 端點。",
            "",
            "",
            false,
            []),
    ];

    public static ProjectAskProvider Get(string? id)
    {
        var key = (id ?? "").Trim();
        return Catalog.FirstOrDefault(p => p.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? Catalog[^1];
    }

    public static string MatchId(string? baseUrl)
    {
        var n = NormalizeUrl(baseUrl);
        if (string.IsNullOrEmpty(n))
            return CustomId;
        foreach (var p in Catalog)
        {
            if (p.Id == CustomId)
                continue;
            if (string.Equals(NormalizeUrl(p.BaseUrl), n, StringComparison.OrdinalIgnoreCase))
                return p.Id;
        }
        return CustomId;
    }

    public static bool CanPullModels(string? baseUrl)
    {
        if (MatchId(baseUrl) == "ollama")
            return true;
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            return false;
        return uri.Port == 11434;
    }

    public static string NormalizeUrl(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        if (u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            u = u[..^"/chat/completions".Length].TrimEnd('/');
        u = u.Replace("://localhost", "://127.0.0.1", StringComparison.OrdinalIgnoreCase);
        return u;
    }

    public static string TitleFor(string? baseUrl, string? fallbackHost = null)
    {
        var id = MatchId(baseUrl);
        if (id != CustomId)
            return Get(id).Title;
        var host = fallbackHost;
        if (string.IsNullOrWhiteSpace(host) && Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri))
            host = uri.Host;
        return string.IsNullOrWhiteSpace(host) ? "自訂" : "自訂（" + host.Trim() + "）";
    }

    public static string SourceIdFor(string? baseUrl)
    {
        var id = MatchId(baseUrl);
        if (id != CustomId)
            return id;
        var n = NormalizeUrl(baseUrl);
        if (string.IsNullOrEmpty(n))
            return CustomId;
        var hash = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(n);
        return "src-" + hash.ToString("x");
    }

    public static IReadOnlyList<ProjectAskSource> Upsert(
        IReadOnlyList<ProjectAskSource> existing,
        string baseUrl,
        string model,
        string? apiKey)
    {
        var url = (baseUrl ?? "").Trim();
        if (string.IsNullOrEmpty(url))
            return existing;
        var id = SourceIdFor(url);
        var title = TitleFor(url);
        var next = new ProjectAskSource(id, title, url, (model ?? "").Trim(), string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
        var list = existing.Where(s =>
            !s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(NormalizeUrl(s.BaseUrl), NormalizeUrl(url), StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, next);
        return list;
    }

    public static IReadOnlyList<ProjectAskSource> Remove(IReadOnlyList<ProjectAskSource> existing, string? id)
    {
        var key = (id ?? "").Trim();
        if (string.IsNullOrEmpty(key))
            return existing;
        return existing.Where(s => !s.Id.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static IReadOnlyList<ProjectAskSource> Parse(JsonNode? node)
    {
        if (node is not JsonArray arr)
            return [];
        var list = new List<ProjectAskSource>();
        foreach (var item in arr)
        {
            if (item is not JsonObject o)
                continue;
            var id = JsonUtil.Str(o["id"]);
            var baseUrl = JsonUtil.Str(o["baseUrl"]);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(baseUrl))
                continue;
            var title = JsonUtil.Str(o["title"]);
            var model = JsonUtil.Str(o["model"]);
            var apiKey = JsonUtil.Str(o["apiKey"]);
            list.Add(new ProjectAskSource(
                id,
                string.IsNullOrEmpty(title) ? TitleFor(baseUrl) : title,
                baseUrl,
                model,
                string.IsNullOrEmpty(apiKey) ? null : apiKey));
        }
        return list;
    }

    public static JsonArray ToJson(IReadOnlyList<ProjectAskSource> sources)
    {
        var arr = new JsonArray();
        foreach (var s in sources)
        {
            var o = new JsonObject
            {
                ["id"] = s.Id,
                ["title"] = s.Title,
                ["baseUrl"] = s.BaseUrl,
                ["model"] = s.Model ?? "",
            };
            if (!string.IsNullOrWhiteSpace(s.ApiKey))
                o["apiKey"] = s.ApiKey.Trim();
            arr.Add(o);
        }
        return arr;
    }
}
