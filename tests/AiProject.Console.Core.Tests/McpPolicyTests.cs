using System.Text.Json.Nodes;
using AiProject.Console.Core.Stack;

namespace AiProject.Console.Core.Tests;

public class McpPolicyTests
{
    [Fact]
    public async Task StopAll_RequiresConfirm()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var denied = await Invoke(ws, "stop_all");
            Assert.Contains("confirm=true", denied);
            Assert.Contains("needConfirm", denied);

            var allowed = await Invoke(ws, "stop_all", new Dictionary<string, string?> { ["confirm"] = "true" });
            Assert.Contains("\"ok\":true", allowed.Replace(" ", ""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Deny_BlocksTool()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var policy = McpPolicy.Defaults() with { Deny = ["stop_all"] };
            var text = await StackToolRouter.InvokeAsync(ws, "stop_all",
                new Dictionary<string, string?> { ["confirm"] = "true" }, policy);
            Assert.Contains("政策禁止", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadOnly_BlocksBuild()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var policy = McpPolicy.Defaults() with { ReadOnly = true };
            var text = await StackToolRouter.InvokeAsync(ws, "build",
                new Dictionary<string, string?> { ["mode"] = "stale" }, policy);
            Assert.Contains("唯讀", text);
            Assert.DoesNotContain("build", StackToolRouter.VisibleTools(policy).Select(t => t.Name));
            Assert.Contains("stack_status", StackToolRouter.VisibleTools(policy).Select(t => t.Name));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AllowList_HidesOthers()
    {
        var policy = McpPolicy.Defaults() with { Allow = ["stack_status", "get_log"] };
        var names = StackToolRouter.VisibleTools(policy).Select(t => t.Name).ToHashSet();
        Assert.Equal(2, names.Count);
        Assert.Contains("stack_status", names);
        Assert.Contains("get_log", names);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ProjectFile_OverridesUserDefaults()
    {
        var root = CreateMini();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".ai_project"));
            File.WriteAllText(Path.Combine(root, ".ai_project", "mcp-policy.json"), """
                { "readOnly": true, "confirm": ["stop_all", "stop_service"] }
                """);
            var policy = McpPolicy.Load(root, McpPolicy.Defaults());
            Assert.True(policy.ReadOnly);
            Assert.Contains("stop_service", policy.Confirm);
            Assert.Equal("project", policy.Source);

            var ws = StackWorkspace.Open(root);
            var text = await StackToolRouter.InvokeAsync(ws, "start_all", policy: policy);
            Assert.Contains("唯讀", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Invoke_WritesAudit_AndListAudit()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            await Invoke(ws, "stack_status");
            var path = McpAuditLog.FilePath(ws.Runtime);
            Assert.True(File.Exists(path));
            var line = File.ReadAllText(path);
            Assert.Contains("stack_status", line);
            Assert.Contains("\"ok\":true", line.Replace(" ", ""));

            var listed = await Invoke(ws, "list_audit");
            Assert.Contains("stack_status", listed);
            Assert.Contains("mcp-audit.jsonl", listed);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DutySummary_Attention_ClearAndProblems()
    {
        Assert.Equal("堆疊正常", DutySummary.Attention(0, 0, 0, null));
        Assert.True(DutySummary.IsClear(0, 0, 0));
        Assert.Equal("離線 2 · 需重編 1 · MCP 拒絕 3（最近 stop_all）",
            DutySummary.Attention(2, 1, 3, "stop_all"));
        Assert.False(DutySummary.IsClear(1, 0, 0));
    }

    [Fact]
    public async Task DutySummary_Tool_ReturnsAttention()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var text = await Invoke(ws, "duty_summary");
            Assert.Contains("attention", text);
            Assert.Contains("offline", text);
            Assert.Contains("staleProjects", text);
            Assert.Contains("auditFails", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadRecent_ParsesArgsAndError()
    {
        var root = CreateMini();
        try
        {
            var ws = StackWorkspace.Open(root);
            var path = McpAuditLog.FilePath(ws.Runtime);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
                {"utc":"2026-08-25T01:00:00Z","tool":"stack_status","ok":true,"ms":12,"args":{}}
                {"utc":"2026-08-25T01:01:00Z","tool":"stop_all","ok":false,"ms":3,"args":{"confirm":"false"},"error":"需要確認"}

                """);
            var (total, entries) = McpAuditLog.ReadRecent(ws.Runtime, 80);
            Assert.Equal(2, total);
            Assert.Equal("stack_status", entries[0].Tool);
            Assert.True(entries[0].Ok);
            Assert.Equal("stop_all", entries[1].Tool);
            Assert.False(entries[1].Ok);
            Assert.Contains("confirm=false", entries[1].ArgsText);
            Assert.Equal("需要確認", entries[1].Error);
            Assert.Equal("拒絕／失敗", entries[1].StatusText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteTemplate_CreatesPolicyFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = McpPolicy.WriteTemplate(root);
            Assert.True(File.Exists(path));
            var obj = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.False(obj["readOnly"]!.GetValue<bool>());
            Assert.Contains("stop_all", obj["confirm"]!.AsArray().Select(n => n!.ToString()));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Playbook_MentionsConfirm()
    {
        Assert.Contains("confirm=true", AiProject.Console.Core.Agents.AgentPlaybook.VerificationHint());
        Assert.Contains("list_audit", AiProject.Console.Core.Agents.AgentPlaybook.ManagerHint());
    }

    static Task<string> Invoke(StackWorkspace ws, string name, IReadOnlyDictionary<string, string?>? args = null) =>
        StackToolRouter.InvokeAsync(ws, name, args, McpPolicy.Defaults());

    static string CreateMini()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-mcp-pol-" + Guid.NewGuid().ToString("N"));
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
            { "profiles": { "http": { "applicationUrl": "http://localhost:5998" } } }
            """);
        File.WriteAllBytes(Path.Combine(proj, "bin", "Debug", "net8.0", "Mini.Api.dll"), [0]);
        return root;
    }
}
