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
            var services = await Invoke(ws, "list_services");
            Assert.Contains("Mini.Api", services);
            var status = await Invoke(ws, "stack_status");
            Assert.Contains("staleProjects", status);
            var freshness = await Invoke(ws, "build_freshness");
            Assert.Contains("Mini.Api", freshness);
            Assert.Contains("\"needsRebuild\":", freshness);
            Assert.Contains("\"badge\":", freshness);
            Assert.Contains("無需重編，全部最新", freshness);
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
            var text = await Invoke(ws, "nope");
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
            var text = await Invoke(ws, "get_log", new Dictionary<string, string?> { ["id"] = id });
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
            Assert.True(McpLaunch.IsLinked(root, McpLaunch.ServerId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ListReferenced_ShowsLinkedAndSuggested()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-mcp-ref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".cursor"));
        try
        {
            File.WriteAllText(McpLaunch.CursorConfigPath(root), """
                { "mcpServers": { "github": { "command": "npx" }, "my-internal": { "command": "node" } } }
                """);
            var list = McpLaunch.ListReferenced(root);
            Assert.Contains(list, s => s.Id == "github" && s.Linked && s.Title == "GitHub");
            Assert.Contains(list, s => s.Id == "my-internal" && s.Linked && s.Title == "my-internal");
            Assert.Contains(list, s => s.Id == McpLaunch.ServerId && !s.Linked && s.Ours);
            Assert.Contains(list, s => s.Id == "context7" && !s.Linked && s.Suggested);
            Assert.Equal(list.Count, list.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteCatalogAndCustom_ThenRemove()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-mcp-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            McpLaunch.WriteCursorConfig(root);
            var github = File.ReadAllText(McpLaunch.WriteCatalogServer(root, "github"));
            Assert.Contains("api.githubcopilot.com/mcp", github);
            Assert.Contains(McpLaunch.ServerId, github);

            var custom = File.ReadAllText(McpLaunch.WriteCustomServer(root, "sentry", "npx", "-y @sentry/mcp-server", null));
            Assert.Contains("sentry", custom);
            Assert.Contains("@sentry/mcp-server", custom);

            var remote = File.ReadAllText(McpLaunch.WriteCustomServer(root, "company-monitor", null, null, "https://mcp.example.internal"));
            Assert.Contains("company-monitor", remote);
            Assert.Contains("https://mcp.example.internal", remote);

            Assert.True(McpLaunch.IsLinked(root, "github"));
            Assert.True(McpLaunch.IsLinked(root, "sentry"));
            McpLaunch.RemoveServer(root, "sentry");
            Assert.False(McpLaunch.IsLinked(root, "sentry"));
            Assert.True(McpLaunch.IsLinked(root, McpLaunch.ServerId));
            Assert.True(McpLaunch.IsValidServerId("company-monitor"));
            Assert.False(McpLaunch.IsValidServerId(McpLaunch.CustomPickerId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DisplayTitle_CoversKnownTools()
    {
        foreach (var tool in StackToolRouter.Tools)
            Assert.False(string.IsNullOrWhiteSpace(StackToolRouter.DisplayTitle(tool.Name)));
        Assert.Equal("值班摘要", StackToolRouter.DisplayTitle("duty_summary"));
        Assert.Equal("停止全部", StackToolRouter.DisplayTitle("stop_all"));
    }

    [Fact]
    public void Playbook_MentionsStackTools()
    {
        Assert.Contains("stack_status", AgentPlaybook.VerificationHint());
        Assert.Contains("build", AgentPlaybook.VerificationHint());
        Assert.Contains("duty_summary", AgentPlaybook.ManagerHint());
    }

    [Fact]
    public void Tools_CoverManagerLoop()
    {
        var names = StackToolRouter.Tools.Select(t => t.Name).ToHashSet();
        foreach (var need in new[] { "duty_summary", "stack_status", "build", "get_log", "start_all", "git_status", "ci_status", "pr_status", "doctor" })
            Assert.Contains(need, names);
    }

    static Task<string> Invoke(StackWorkspace ws, string name, IReadOnlyDictionary<string, string?>? args = null) =>
        StackToolRouter.InvokeAsync(ws, name, args, McpPolicy.Defaults());

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
