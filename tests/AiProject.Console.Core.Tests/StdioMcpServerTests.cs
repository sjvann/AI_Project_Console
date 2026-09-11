using System.Text;
using System.Text.Json.Nodes;
using AiProject.Console.Core;
using AiProject.Console.Core.Stack;

namespace AiProject.Console.Core.Tests;

public class StdioMcpServerTests
{
    [Fact]
    public async Task NewlineInitialize_RepliesWithoutContentLength()
    {
        var root = CreateMini();
        try
        {
            var input = Encode("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"0"}}}

                """);
            using var output = new MemoryStream();
            var ws = StackWorkspace.Open(root);
            await StdioMcpServer.RunAsync(ws, input, output);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.DoesNotContain("Content-Length:", text);
            Assert.Contains("\"protocolVersion\":\"2024-11-05\"", text);
            Assert.Contains("\"name\":\"ai-project-console\"", text);
            Assert.EndsWith("\n", text);
            AssertBrandIcons(text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ContentLengthInitialize_StillWorks()
    {
        var root = CreateMini();
        try
        {
            var body = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""";
            var framed = Encoding.ASCII.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n")
                .Concat(Encoding.UTF8.GetBytes(body))
                .ToArray();
            using var input = new MemoryStream(framed);
            using var output = new MemoryStream();
            await StdioMcpServer.RunAsync(StackWorkspace.Open(root), input, output);

            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.StartsWith("Content-Length:", text);
            Assert.Contains("\"name\":\"ai-project-console\"", text);
            AssertBrandIcons(text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task NewlineToolsList_AfterInitializedNotification()
    {
        var root = CreateMini();
        try
        {
            var input = Encode("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}

                """);
            using var output = new MemoryStream();
            await StdioMcpServer.RunAsync(StackWorkspace.Open(root), input, output);

            var lines = Encoding.UTF8.GetString(output.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("duty_summary", lines[1]);
            Assert.Contains("stack_status", lines[1]);
            Assert.DoesNotContain("Content-Length:", lines[1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadMessage_DetectsNewlineJson()
    {
        using var input = Encode("""{"jsonrpc":"2.0","id":1,"method":"ping"}""" + "\n");
        var read = await StdioMcpServer.ReadMessageAsync(input, framing: null, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(McpStdioFraming.Newline, read.Value.Framing);
        Assert.Contains("\"method\":\"ping\"", read.Value.Body);
    }

    [Fact]
    public async Task ReadMessage_DetectsContentLength()
    {
        var body = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";
        var framed = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n")
            .Concat(Encoding.UTF8.GetBytes(body))
            .ToArray();
        using var input = new MemoryStream(framed);
        var read = await StdioMcpServer.ReadMessageAsync(input, framing: null, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Equal(McpStdioFraming.ContentLength, read.Value.Framing);
        Assert.Equal(body, read.Value.Body);
    }

    static MemoryStream Encode(string text) => new(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")));

    static void AssertBrandIcons(string text)
    {
        var idx = text.IndexOf('{');
        Assert.True(idx >= 0);
        var json = text[idx..].Trim();
        var root = JsonNode.Parse(json)!["result"]!["serverInfo"]!;
        var icons = root["icons"]!.AsArray();
        Assert.Equal(2, icons.Count);
        Assert.Equal("image/png", icons[0]!["mimeType"]!.GetValue<string>());
        Assert.StartsWith("data:image/png;base64,", icons[0]!["src"]!.GetValue<string>());
        Assert.Equal("image/svg+xml", icons[1]!["mimeType"]!.GetValue<string>());
        Assert.Equal(AppInfo.Product, root["title"]!.GetValue<string>());
    }

    static string CreateMini()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stdio-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", "Mini.Api");
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        Directory.CreateDirectory(Path.Combine(proj, "bin", "Debug", "net8.0"));
        File.WriteAllText(Path.Combine(proj, "Mini.Api.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(proj, "Program.cs"), "Console.WriteLine();");
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
            { "profiles": { "http": { "applicationUrl": "http://localhost:5997" } } }
            """);
        File.WriteAllBytes(Path.Combine(proj, "bin", "Debug", "net8.0", "Mini.Api.dll"), [0]);
        return root;
    }
}
