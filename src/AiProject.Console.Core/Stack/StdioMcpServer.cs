using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiProject.Console.Core.Stack;

/// <summary>
/// 最小 JSON-RPC MCP stdio（Content-Length 框架），讓 Cursor／Claude Code 呼叫堆疊工具。
/// </summary>
public static class StdioMcpServer
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task RunAsync(StackWorkspace workspace, CancellationToken ct = default)
    {
        using var input = System.Console.OpenStandardInput();
        using var output = System.Console.OpenStandardOutput();
        while (!ct.IsCancellationRequested)
        {
            var body = await ReadMessageAsync(input, ct).ConfigureAwait(false);
            if (body is null)
                return;
            JsonNode? msg;
            try { msg = JsonNode.Parse(body); }
            catch { continue; }
            if (msg is not JsonObject obj)
                continue;
            var method = obj["method"]?.GetValue<string>();
            var id = obj["id"];
            if (string.IsNullOrEmpty(method))
                continue;
            if (id is null)
                continue;

            object result;
            object? error = null;
            try
            {
                result = method switch
                {
                    "initialize" => InitializeResult(),
                    "ping" => new JsonObject(),
                    "tools/list" => ToolsList(workspace),
                    "tools/call" => await ToolsCallAsync(workspace, obj["params"]).ConfigureAwait(false),
                    _ => throw new InvalidOperationException("Method not found: " + method),
                };
            }
            catch (Exception ex)
            {
                result = new JsonObject();
                error = new { code = -32601, message = ex.Message };
            }

            JsonObject reply = error is null
                ? new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id?.DeepClone(),
                    ["result"] = ToNode(result),
                }
                : new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id?.DeepClone(),
                    ["error"] = ToNode(error),
                };
            await WriteMessageAsync(output, reply.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), ct)
                .ConfigureAwait(false);
        }
    }

    static object InitializeResult() => new
    {
        protocolVersion = "2024-11-05",
        capabilities = new { tools = new { } },
        serverInfo = new { name = "ai-project-console", version = AppInfo.Version },
        instructions = "本機多服務堆疊操作台。值班先 duty_summary；修碼後 build；事故看 get_log。stop_all 必須帶 confirm=true。政策拒絕的工具不會出現在清單。",
    };

    static object ToolsList(StackWorkspace workspace)
    {
        var policy = McpPolicy.Load(workspace.Root);
        var tools = StackToolRouter.VisibleTools(policy).Select(t =>
        {
            var props = new Dictionary<string, object>();
            var required = new List<string>();
            foreach (var token in (t.ArgsHint ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                props[token] = new { type = "string", description = token };
                if (token is "id" or "path")
                    required.Add(token);
            }
            return new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = new
                {
                    type = "object",
                    properties = props,
                    required,
                },
            };
        });
        return new { tools };
    }

    static async Task<object> ToolsCallAsync(StackWorkspace workspace, JsonNode? rawParams)
    {
        var name = rawParams?["name"]?.GetValue<string>() ?? "";
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (rawParams?["arguments"] is JsonObject args)
        {
            foreach (var kv in args)
                map[kv.Key] = kv.Value?.ToString();
        }
        var text = await StackToolRouter.InvokeAsync(workspace, name, map).ConfigureAwait(false);
        return new
        {
            content = new object[] { new { type = "text", text } },
        };
    }

    static JsonNode? ToNode(object value)
    {
        if (value is JsonNode node)
            return node;
        return JsonNode.Parse(JsonSerializer.Serialize(value, JsonOpts));
    }

    static async Task<string?> ReadMessageAsync(Stream input, CancellationToken ct)
    {
        var header = new MemoryStream();
        var buf = new byte[1];
        var matched = 0;
        var crlfcrlf = new byte[] { 13, 10, 13, 10 };
        while (matched < 4)
        {
            var n = await input.ReadAsync(buf.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
                return null;
            header.WriteByte(buf[0]);
            matched = buf[0] == crlfcrlf[matched] ? matched + 1 : (buf[0] == 13 ? 1 : 0);
        }
        var headers = Encoding.UTF8.GetString(header.ToArray());
        var length = 0;
        foreach (var line in headers.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(t["Content-Length:".Length..].Trim(), out var n))
                length = n;
        }
        if (length <= 0)
            return null;
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await input.ReadAsync(body.AsMemory(read, length - read), ct).ConfigureAwait(false);
            if (n == 0)
                return null;
            read += n;
        }
        return Encoding.UTF8.GetString(body);
    }

    static async Task WriteMessageAsync(Stream output, string json, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await output.WriteAsync(header, ct).ConfigureAwait(false);
        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }
}
