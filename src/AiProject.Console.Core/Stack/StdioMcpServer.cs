using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiProject.Console.Core.Stack;

internal enum McpStdioFraming
{
    Newline,
    ContentLength,
}

/// <summary>
/// 最小 JSON-RPC MCP stdio。讀寫同時支援 MCP 規格的換行分隔 JSON，以及舊的 Content-Length 框架。
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
        await RunAsync(workspace, input, output, ct).ConfigureAwait(false);
    }

    internal static async Task RunAsync(
        StackWorkspace workspace,
        Stream input,
        Stream output,
        CancellationToken ct = default)
    {
        McpStdioFraming? framing = null;
        while (!ct.IsCancellationRequested)
        {
            var read = await ReadMessageAsync(input, framing, ct).ConfigureAwait(false);
            if (read is null)
                return;
            framing ??= read.Value.Framing;
            JsonNode? msg;
            try { msg = JsonNode.Parse(read.Value.Body); }
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
            await WriteMessageAsync(
                    output,
                    reply.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
                    framing.Value,
                    ct)
                .ConfigureAwait(false);
        }
    }

    static object InitializeResult() => new
    {
        protocolVersion = "2024-11-05",
        capabilities = new { tools = new { } },
        serverInfo = new
        {
            name = "ai-project-console",
            title = AppInfo.Product,
            version = AppInfo.Version,
            websiteUrl = "https://github.com/" + AppInfo.GitHubSlug,
            icons = McpServerIcons.ForInitialize(),
        },
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

    internal static async Task<(string Body, McpStdioFraming Framing)?> ReadMessageAsync(
        Stream input,
        McpStdioFraming? framing,
        CancellationToken ct)
    {
        if (framing == McpStdioFraming.Newline)
        {
            var line = await ReadNewlineMessageAsync(input, prefix: null, ct).ConfigureAwait(false);
            return line is null ? null : (line, McpStdioFraming.Newline);
        }

        if (framing == McpStdioFraming.ContentLength)
        {
            var body = await ReadContentLengthMessageAsync(input, prefix: null, ct).ConfigureAwait(false);
            return body is null ? null : (body, McpStdioFraming.ContentLength);
        }

        var first = await ReadSignificantByteAsync(input, ct).ConfigureAwait(false);
        if (first is null)
            return null;
        if (first.Value == (byte)'{')
        {
            var line = await ReadNewlineMessageAsync(input, first.Value, ct).ConfigureAwait(false);
            return line is null ? null : (line, McpStdioFraming.Newline);
        }

        var headerBody = await ReadContentLengthMessageAsync(input, first.Value, ct).ConfigureAwait(false);
        return headerBody is null ? null : (headerBody, McpStdioFraming.ContentLength);
    }

    static readonly byte[] NewLineBytes = [(byte)'\n'];

    internal static async Task WriteMessageAsync(
        Stream output,
        string json,
        McpStdioFraming framing,
        CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (framing == McpStdioFraming.Newline)
        {
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
            await output.WriteAsync(NewLineBytes, ct).ConfigureAwait(false);
        }
        else
        {
            var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
            await output.WriteAsync(header, ct).ConfigureAwait(false);
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    static async Task<byte?> ReadSignificantByteAsync(Stream input, CancellationToken ct)
    {
        var buf = new byte[1];
        while (true)
        {
            var n = await input.ReadAsync(buf.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
                return null;
            if (!IsWhitespace(buf[0]))
                return buf[0];
        }
    }

    static bool IsWhitespace(byte b) => b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    static async Task<string?> ReadNewlineMessageAsync(Stream input, byte? prefix, CancellationToken ct)
    {
        while (true)
        {
            var line = await ReadLineAsync(input, prefix, ct).ConfigureAwait(false);
            prefix = null;
            if (line is null)
                return null;
            if (line.Length == 0)
                continue;
            return line;
        }
    }

    static async Task<string?> ReadLineAsync(Stream input, byte? prefix, CancellationToken ct)
    {
        var ms = new MemoryStream();
        if (prefix is byte p)
            ms.WriteByte(p);
        var buf = new byte[1];
        while (true)
        {
            var n = await input.ReadAsync(buf.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (ms.Length == 0)
                    return null;
                break;
            }
            if (buf[0] == (byte)'\n')
                break;
            if (buf[0] != (byte)'\r')
                ms.WriteByte(buf[0]);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static async Task<string?> ReadContentLengthMessageAsync(Stream input, byte? prefix, CancellationToken ct)
    {
        var header = new MemoryStream();
        if (prefix is byte p)
            header.WriteByte(p);
        var buf = new byte[1];
        var matched = prefix == 13 ? 1 : 0;
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
}
