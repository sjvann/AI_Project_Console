using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Stack;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed record ProjectAskOptions(string BaseUrl, string Model, string? ApiKey = null);

public sealed record ProjectAskChatItem(string Role, string Text, string? Tool = null);

public sealed record ProjectAskProbeResult(
    bool Ok,
    bool ModelFound,
    string Message,
    IReadOnlyList<string> Models);

public sealed record ProjectAskPullResult(bool Ok, string Message);

/// <summary>
/// 控制台內專案問答：OpenAI 相容 HTTP + 本機唯讀堆疊工具。不佔用桌面 JobBusy。
/// </summary>
public static class ProjectAskService
{
    public const string DefaultBaseUrl = "http://127.0.0.1:11434/v1";
    public const string DefaultModel = "llama3.2";
    public const int MaxRounds = 6;

    public static bool IsConfigured(string? baseUrl, string? model) =>
        !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(model);

    public static string ChatCompletionsUrl(string baseUrl)
    {
        var u = (baseUrl ?? "").Trim().TrimEnd('/');
        if (u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return u;
        return u + "/chat/completions";
    }

    public static string ModelsUrl(string baseUrl)
    {
        var u = (baseUrl ?? "").Trim().TrimEnd('/');
        if (u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            u = u[..^"/chat/completions".Length].TrimEnd('/');
        if (u.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return u;
        return u + "/models";
    }

    public static string NativeOrigin(string baseUrl)
    {
        var u = (baseUrl ?? "").Trim().TrimEnd('/');
        if (u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            u = u[..^"/chat/completions".Length].TrimEnd('/');
        if (u.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            u = u[..^"/models".Length].TrimEnd('/');
        if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            u = u[..^"/v1".Length].TrimEnd('/');
        return u;
    }

    public static string PullUrl(string baseUrl) => NativeOrigin(baseUrl) + "/api/pull";

    public static bool ModelInList(string? model, IReadOnlyList<string> models)
    {
        var m = (model ?? "").Trim();
        if (string.IsNullOrEmpty(m) || models.Count == 0)
            return false;
        foreach (var id in models)
        {
            if (id.Equals(m, StringComparison.OrdinalIgnoreCase))
                return true;
            if (id.StartsWith(m + ":", StringComparison.OrdinalIgnoreCase))
                return true;
            var slash = id.LastIndexOf('/');
            if (slash >= 0 && id[(slash + 1)..].Equals(m, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static IReadOnlyList<string> PullCandidates(
        string? baseUrl,
        string? currentModel,
        IReadOnlyList<string> installed)
    {
        if (!ProjectAskProviders.CanPullModels(baseUrl))
            return [];
        var provider = ProjectAskProviders.Get(ProjectAskProviders.MatchId(baseUrl));
        var suggested = provider.SuggestedModels.Count > 0
            ? provider.SuggestedModels
            : ProjectAskProviders.Get("ollama").SuggestedModels;
        var have = installed ?? [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        void Add(string? value)
        {
            var t = (value ?? "").Trim();
            if (string.IsNullOrEmpty(t) || !seen.Add(t))
                return;
            if (!ProjectAskModelSupport.SupportsTools(t))
                return;
            if (have.Count > 0 && ModelInList(t, have))
                return;
            list.Add(t);
        }
        foreach (var m in suggested)
            Add(m);
        Add(currentModel);
        return list;
    }

    /// <summary>測 OpenAI 相容端點：先 GET /models，不行再送極短 chat。</summary>
    public static async Task<ProjectAskProbeResult> ProbeAsync(
        ProjectAskOptions options,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new ProjectAskProbeResult(false, false, "請先填 Base URL。", []);

        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromSeconds(8);
        try
        {
            var listed = await TryListModelsAsync(client, options, ct).ConfigureAwait(false);
            if (listed is not null)
                return listed;
            if (string.IsNullOrWhiteSpace(options.Model))
                return Fail("此端點沒有模型清單。請填模型名稱後再測。");

            await PingChatAsync(client, options, ct).ConfigureAwait(false);
            if (ProjectAskModelSupport.RejectReason(options.Model) is { } reason)
                return new ProjectAskProbeResult(false, false, reason, []);
            return new ProjectAskProbeResult(
                true,
                true,
                "來源正常 · 端點有回應，但沒有模型清單，請自行確認模型名稱。",
                []);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("連線逾時。本機請確認服務已啟動；雲端請檢查網址。");
        }
        catch (HttpRequestException ex)
        {
            return Fail(FormatHttpError(options.BaseUrl, ex));
        }
        catch (InvalidOperationException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>對本機 Ollama POST /api/pull，串流進度。不佔用桌面 JobBusy。</summary>
    public static async Task<ProjectAskPullResult> PullAsync(
        ProjectAskOptions options,
        string? model = null,
        Action<string>? onStatus = null,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        var name = (model ?? options.Model ?? "").Trim();
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            return new ProjectAskPullResult(false, "請先填 Base URL。");
        if (string.IsNullOrWhiteSpace(name))
            return new ProjectAskPullResult(false, "請先填要 pull 的模型。");
        if (ProjectAskModelSupport.RejectReason(name) is { } reason)
            return new ProjectAskPullResult(false, reason);
        if (!ProjectAskProviders.CanPullModels(options.BaseUrl))
            return new ProjectAskPullResult(false, "只有本機 Ollama 能在控制台內 pull。");

        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = Timeout.InfiniteTimeSpan;
        try
        {
            var body = new JsonObject
            {
                ["model"] = name,
                ["name"] = name,
                ["stream"] = true,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, PullUrl(options.BaseUrl));
            req.Content = new StringContent(body.ToJsonString(JsonUtil.Options), Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());

            onStatus?.Invoke("開始 pull " + name + "…");
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var raw = "";
            if (!res.IsSuccessStatusCode)
            {
                raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return new ProjectAskPullResult(false, FormatPullHttpError((int)res.StatusCode, raw));
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            var lastStatus = "";
            string? line;
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                JsonObject? obj;
                try { obj = JsonNode.Parse(line) as JsonObject; }
                catch (System.Text.Json.JsonException)
                {
                    lastStatus = Truncate(line.Trim(), 120);
                    onStatus?.Invoke(lastStatus);
                    continue;
                }
                if (obj is null)
                    continue;
                if (obj["error"] is JsonNode errNode)
                {
                    var err = JsonUtil.Str(errNode);
                    if (errNode is JsonObject errObj)
                        err = JsonUtil.Pick(JsonUtil.Str(errObj["message"]), err);
                    return new ProjectAskPullResult(false, string.IsNullOrEmpty(err) ? "pull 失敗。" : err);
                }

                lastStatus = FormatPullStatus(name, obj);
                onStatus?.Invoke(lastStatus);
                var status = JsonUtil.Str(obj["status"]);
                if (status.Equals("success", StringComparison.OrdinalIgnoreCase)
                    || JsonUtil.Str(obj["completed"]).Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProjectAskPullResult(true, "已 pull " + name + "。");
                }
            }

            return string.IsNullOrEmpty(lastStatus)
                ? new ProjectAskPullResult(false, "pull 沒有回傳進度。請確認 Ollama 已啟動。")
                : new ProjectAskPullResult(false, "pull 中斷：" + lastStatus);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ProjectAskPullResult(false, "已取消下載。");
        }
        catch (OperationCanceledException)
        {
            return new ProjectAskPullResult(false, "pull 逾時。請確認 Ollama 已啟動後再試。");
        }
        catch (HttpRequestException ex)
        {
            return new ProjectAskPullResult(false, FormatHttpError(options.BaseUrl, ex));
        }
        catch (Exception ex)
        {
            return new ProjectAskPullResult(false, Truncate(ex.Message, 180));
        }
    }

    public static IReadOnlyList<StackToolSpec> AskTools =>
        StackToolRouter.Tools.Where(t =>
            McpPolicy.ReadOnlyTools.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();

    public static string SystemPrompt(string projectName, string root, string dutySnapshot) =>
        "你是 AI_Project 控制台的專案問答助手。只回答目前開啟專案的本機堆疊、編譯狀態、服務 Log、MCP 審計、文件（docs/）與環境體檢。\n"
        + "不要當通用聊天，不要改程式碼，不要把話題帶到這個倉以外。\n"
        + "先呼叫工具取得事實，再以繁體中文簡短回答。編譯與啟停請使用者用控制台按鈕。\n"
        + "編譯狀態以工具為準，且必須與畫面徽章相同：badge「最新」或 status=fresh 或 needsRebuild=false 表示無需重編；"
        + "「需重編」／stale、「未建置」／unbuilt 才要重編。值班摘要的 staleProjects／需重編數字必須與回答一致。"
        + "不要把「已與 DLL 一致」或「幾分鐘前」的時間戳理解成要重編。\n"
        + "專案：" + projectName + "\n根目錄：" + root + "\n目前值班摘要：\n" + dutySnapshot;

    public static async Task<string> AskAsync(
        StackWorkspace workspace,
        string question,
        IReadOnlyList<ProjectAskChatItem> history,
        ProjectAskOptions options,
        Action<string>? onStatus = null,
        Action<string>? onTool = null,
        HttpMessageHandler? handler = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured(options.BaseUrl, options.Model))
            throw new InvalidOperationException("請在設定填專案問答的 Base URL 與模型。");
        if (ProjectAskModelSupport.RejectReason(options.Model) is { } reason)
            throw new InvalidOperationException(reason);
        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("請輸入問題。");

        onStatus?.Invoke("讀取值班摘要…");
        var duty = await workspace.DutySummaryAsync().ConfigureAwait(false);
        var messages = new JsonArray
        {
            Msg("system", SystemPrompt(workspace.Catalog.Name, workspace.Root, duty)),
        };
        foreach (var item in history)
        {
            if (item.Role is "user" or "assistant" && string.IsNullOrEmpty(item.Tool) && !string.IsNullOrWhiteSpace(item.Text))
                messages.Add(Msg(item.Role, item.Text));
        }
        messages.Add(Msg("user", question.Trim()));

        using var client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        client.Timeout = TimeSpan.FromMinutes(2);
        var url = ChatCompletionsUrl(options.BaseUrl);
        var tools = BuildTools();

        for (var round = 0; round < MaxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();
            onStatus?.Invoke(round == 0 ? "思考中…" : "繼續思考…");
            var body = new JsonObject
            {
                ["model"] = options.Model.Trim(),
                ["messages"] = messages.DeepClone(),
                ["tools"] = tools.DeepClone(),
            };
            var reply = await PostChatAsync(client, url, options.ApiKey, body, ct).ConfigureAwait(false);
            var message = reply["choices"]?[0]?["message"] as JsonObject
                ?? throw new InvalidOperationException("模型沒有回傳 message。");
            messages.Add(message.DeepClone());

            var calls = message["tool_calls"] as JsonArray;
            if (calls is { Count: > 0 })
            {
                foreach (var node in calls)
                {
                    ct.ThrowIfCancellationRequested();
                    if (node is not JsonObject call)
                        continue;
                    var fn = call["function"] as JsonObject;
                    var name = JsonUtil.Str(fn?["name"]);
                    if (string.IsNullOrEmpty(name))
                        continue;
                    var callId = JsonUtil.Str(call["id"]);
                    if (string.IsNullOrEmpty(callId))
                        callId = "call_" + Guid.NewGuid().ToString("N")[..8];
                    onTool?.Invoke(name);
                    onStatus?.Invoke("已呼叫 " + StackToolRouter.DisplayTitle(name) + "…");
                    var args = ParseArgs(fn?["arguments"]);
                    var result = await StackToolRouter.InvokeAsync(workspace, name, args, ct: ct).ConfigureAwait(false);
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = callId,
                        ["content"] = Truncate(result, 8000),
                    });
                }
                continue;
            }

            var text = JsonUtil.Str(message["content"]);
            return string.IsNullOrWhiteSpace(text) ? "（沒有回覆）" : text.Trim();
        }

        return "已達工具呼叫上限，請再問一次或縮小問題。";
    }

    static async Task<JsonObject> PostChatAsync(
        HttpClient client,
        string url,
        string? apiKey,
        JsonObject body,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(body.ToJsonString(JsonUtil.Options), Encoding.UTF8, "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(FormatChatError((int)res.StatusCode, raw));
        if (JsonNode.Parse(raw) is not JsonObject obj)
            throw new InvalidOperationException("模型回應不是 JSON 物件。");
        if (obj["error"] is JsonObject err)
            throw new InvalidOperationException(JsonUtil.Str(err["message"]) is { Length: > 0 } msg ? msg : err.ToJsonString());
        return obj;
    }

    static JsonArray BuildTools()
    {
        var arr = new JsonArray();
        foreach (var spec in AskTools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = spec.Name,
                    ["description"] = spec.Description,
                    ["parameters"] = ParametersFor(spec),
                },
            });
        }
        return arr;
    }

    static JsonObject ParametersFor(StackToolSpec spec)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var part in spec.ArgsHint.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            props[part] = new JsonObject
            {
                ["type"] = part == "tail" ? "integer" : "string",
                ["description"] = part == "id" ? "服務 id" : part,
            };
            if (part is "id")
                required.Add(part);
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false,
        };
        if (required.Count > 0)
            schema["required"] = required;
        return schema;
    }

    static Dictionary<string, string?> ParseArgs(JsonNode? node)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var obj = node as JsonObject;
        if (obj is null && node is JsonValue)
        {
            var raw = JsonUtil.Str(node);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                try { obj = JsonNode.Parse(raw) as JsonObject; }
                catch (System.Text.Json.JsonException) { obj = null; }
            }
        }
        if (obj is null)
            return dict;
        foreach (var kv in obj)
            dict[kv.Key] = kv.Value is JsonValue ? JsonUtil.Str(kv.Value) : kv.Value?.ToJsonString();
        return dict;
    }

    static JsonObject Msg(string role, string content) =>
        new()
        {
            ["role"] = role,
            ["content"] = content,
        };

    static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    static async Task<ProjectAskProbeResult?> TryListModelsAsync(
        HttpClient client,
        ProjectAskOptions options,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ModelsUrl(options.BaseUrl));
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());
        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if ((int)res.StatusCode is 404 or 405)
            return null;
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException(FormatStatusError((int)res.StatusCode, raw));
        JsonObject? obj;
        try { obj = JsonNode.Parse(raw) as JsonObject; }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("模型清單不是 JSON 物件。");
        }
        if (obj is null)
            throw new InvalidOperationException("模型清單不是 JSON 物件。");
        if (obj["error"] is JsonObject err)
            throw new InvalidOperationException(JsonUtil.Str(err["message"]) is { Length: > 0 } msg ? msg : err.ToJsonString());

        var listed = ParseModelIds(obj);
        var models = ProjectAskModelSupport.FilterToolCapable(listed);
        var skipped = listed.Count - models.Count;
        var wanted = options.Model.Trim();
        var found = ModelInList(wanted, models);
        var installed = ModelInList(wanted, listed);
        if (listed.Count == 0)
        {
            return new ProjectAskProbeResult(
                true,
                false,
                "來源正常 · 已連上，但清單是空的。請確認該端點已載入模型。",
                []);
        }

        if (models.Count == 0)
        {
            return new ProjectAskProbeResult(
                false,
                false,
                "已連上，但沒有支援工具呼叫的模型。" + ProjectAskModelSupport.NeedToolsHint,
                []);
        }

        var sample = string.Join("、", models.Take(4));
        var skipNote = skipped > 0 ? " 已略過不支援工具的模型。" : "";
        if (string.IsNullOrEmpty(wanted))
        {
            return new ProjectAskProbeResult(
                true,
                false,
                $"來源正常 · 已連上，共 {models.Count} 個可用模型。請從下方選一個。" + skipNote,
                models);
        }
        if (found)
        {
            return new ProjectAskProbeResult(
                true,
                true,
                $"來源正常 · 已連上，清單含 {wanted}（共 {models.Count} 個可用）。" + skipNote,
                models);
        }

        if (installed)
        {
            return new ProjectAskProbeResult(
                false,
                false,
                ProjectAskModelSupport.RejectReason(wanted) ?? ("此模型不支援工具呼叫。" + ProjectAskModelSupport.NeedToolsHint),
                models);
        }

        return new ProjectAskProbeResult(
            true,
            false,
            $"已連上，但清單沒有 {wanted}。可改選下方模型，或先 pull。目前例如：{sample}",
            models);
    }

    static async Task PingChatAsync(HttpClient client, ProjectAskOptions options, CancellationToken ct)
    {
        var url = ChatCompletionsUrl(options.BaseUrl);
        var body = new JsonObject
        {
            ["model"] = options.Model.Trim(),
            ["messages"] = new JsonArray { Msg("user", "ping") },
            ["max_tokens"] = 1,
        };
        await PostChatAsync(client, url, options.ApiKey, body, ct).ConfigureAwait(false);
    }

    static IReadOnlyList<string> ParseModelIds(JsonObject obj)
    {
        var ids = new List<string>();
        var data = obj["data"] as JsonArray ?? obj["models"] as JsonArray;
        if (data is null)
            return ids;
        foreach (var node in data)
        {
            var id = node is JsonObject item
                ? JsonUtil.Pick(JsonUtil.Str(item["id"]), JsonUtil.Str(item["name"]))
                : JsonUtil.Str(node);
            if (!string.IsNullOrEmpty(id))
                ids.Add(id);
        }
        return ids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static ProjectAskProbeResult Fail(string message) =>
        new(false, false, message, []);

    static string FormatChatError(int status, string raw)
    {
        if (ProjectAskModelSupport.LooksLikeToolsUnsupportedError(raw))
            return "此模型不支援工具呼叫。" + ProjectAskModelSupport.NeedToolsHint;
        return FormatStatusError(status, raw);
    }

    static string FormatStatusError(int status, string raw)
    {
        var detail = Truncate(raw.Trim(), 180);
        return status switch
        {
            401 or 403 => "API key 被拒。雲端請填正確金鑰；本機 Ollama 通常可留空。",
            404 => "端點沒有模型清單（HTTP 404）。請確認 Base URL 是否含 /v1。",
            _ => $"模型 HTTP {status}：{detail}",
        };
    }

    static string FormatHttpError(string baseUrl, HttpRequestException ex)
    {
        var host = Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri) ? uri.Host : baseUrl;
        var text = ex.InnerException?.Message ?? ex.Message;
        if (text.Contains("refused", StringComparison.OrdinalIgnoreCase)
            || text.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || text.Contains("無法連線", StringComparison.OrdinalIgnoreCase)
            || text.Contains("連線嘗試失敗", StringComparison.OrdinalIgnoreCase))
        {
            return $"連不上 {host}。本機 Ollama 請先啟動；LM Studio 請開啟本機伺服器。";
        }
        return $"連不上 {host}：{Truncate(text, 180)}";
    }

    static string FormatPullHttpError(int status, string raw)
    {
        if (status is 404)
            return "此端點沒有 Ollama pull API（HTTP 404）。請確認是本機 Ollama 且已啟動。";
        return FormatStatusError(status, raw);
    }

    static string FormatPullStatus(string model, JsonObject obj)
    {
        var total = JsonLong(obj["total"]);
        var completed = JsonLong(obj["completed"]);
        if (total > 0)
        {
            var pct = (int)Math.Min(100, completed * 100 / total);
            return $"{model} {pct}%（{FormatBytes(completed)} / {FormatBytes(total)}）";
        }
        var status = JsonUtil.Str(obj["status"]);
        return string.IsNullOrEmpty(status) ? "正在 pull " + model + "…" : model + "：" + status;
    }

    static long JsonLong(JsonNode? node)
    {
        if (node is not JsonValue v)
            return 0;
        if (v.TryGetValue<long>(out var l))
            return l;
        if (v.TryGetValue<int>(out var i))
            return i;
        if (v.TryGetValue<double>(out var d))
            return (long)d;
        return long.TryParse(JsonUtil.Str(v), out var parsed) ? parsed : 0;
    }

    static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("0") + " KB";
        if (bytes < 1024L * 1024 * 1024)
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.00") + " GB";
    }
}
