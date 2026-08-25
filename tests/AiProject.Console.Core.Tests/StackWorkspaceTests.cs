using AiProject.Console.Core.Agents;
using AiProject.Console.Core.Stack;

namespace AiProject.Console.Core.Tests;

public class StackWorkspaceTests
{
    [Fact]
    public async Task Router_ListServices_And_Status()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var services = await StackToolRouter.InvokeAsync(ws, "list_services");
            Assert.Contains("Mini.Api", services);
            var status = await StackToolRouter.InvokeAsync(ws, "stack_status");
            Assert.Contains("staleProjects", status);
            var freshness = await StackToolRouter.InvokeAsync(ws, "build_freshness");
            Assert.Contains("Mini.Api", freshness);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Router_UnknownTool_ReturnsError()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var text = await StackToolRouter.InvokeAsync(ws, "nope");
            Assert.Contains("未知工具", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Router_GetLog_MissingFile()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            Assert.NotEmpty(ws.Catalog.Services);
            var id = ws.Catalog.Services[0].Id;
            var text = await StackToolRouter.InvokeAsync(ws, "get_log", new Dictionary<string, string?> { ["id"] = id });
            Assert.Contains("尚無 Log", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void McpSnippet_ContainsServerId()
    {
        var json = McpLaunch.CursorSnippet(@"E:\demo");
        Assert.Contains(McpLaunch.ServerId, json);
        Assert.Contains("--root", json);
    }

    [Fact]
    public void WriteCursorConfig_MergesServer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = McpLaunch.WriteCursorConfig(root);
            Assert.True(File.Exists(path));
            Assert.Contains(McpLaunch.ServerId, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Playbook_MentionsStackTools()
    {
        Assert.Contains("stack_status", AgentPlaybook.VerificationHint());
        Assert.Contains("build", AgentPlaybook.VerificationHint());
    }

    [Fact]
    public void Tools_CoverManagerLoop()
    {
        var names = StackToolRouter.Tools.Select(t => t.Name).ToHashSet();
        foreach (var need in new[] { "stack_status", "build", "get_log", "start_all", "git_status", "doctor" })
            Assert.Contains(need, names);
    }

    static string CreateMini()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-" + Guid.NewGuid().ToString("N"));
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
            { "profiles": { "http": { "applicationUrl": "http://localhost:5999" } } }
            """);
        File.WriteAllBytes(Path.Combine(proj, "bin", "Debug", "net8.0", "Mini.Api.dll"), [0]);
        return root;
    }
}
