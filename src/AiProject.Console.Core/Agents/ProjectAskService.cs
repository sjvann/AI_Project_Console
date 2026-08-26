using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Stack;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Agents;

public sealed record ProjectAskOptions(string BaseUrl, string Model, string? ApiKey = null);

public sealed record ProjectAskChatItem(string Role, string Text, string? Tool = null);

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

    public static IReadOnlyList<StackToolSpec> AskTools =>
        StackToolRouter.Tools.Where(t =>
            McpPolicy.ReadOnlyTools.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList();

    public static string SystemPrompt(string projectName, string root, string dutySnapshot) =>
        "你是 AI_Project 控制台的專案問答助手。只回答目前開啟專案的本機堆疊、編譯狀態、服務 Log、MCP 審計與環境體檢。\n"
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
            throw new InvalidOperationException($"模型 HTTP {(int)res.StatusCode}：{Truncate(raw, 400)}");
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
}
