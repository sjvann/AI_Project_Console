using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Stack;

public sealed record ProjectMcpServer(
    string Id,
    string Title,
    string Hint,
    bool Linked,
    bool Ours,
    bool Suggested);

public static class McpLaunch
{
    public const string ServerId = "ai-project-console";

    public static readonly IReadOnlyList<(string Id, string Title, string Hint)> SuggestedServers =
    [
        (ServerId, "本控制台", "堆疊、編譯、啟停、Log"),
        ("github", "GitHub", "PR／Issue／Actions"),
        ("context7", "Context7", "套件文件"),
    ];

    public static string CursorConfigPath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot), ".cursor", "mcp.json");

    public static string? FindMcpProject()
    {
        foreach (var start in new[]
                 {
                     AppContext.BaseDirectory,
                     Directory.GetCurrentDirectory(),
                 })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var proj = Path.Combine(dir.FullName, "src", "AiProject.Console.Mcp", "AiProject.Console.Mcp.csproj");
                if (File.Exists(proj))
                    return Path.GetFullPath(proj);
                dir = dir.Parent;
            }
        }
        return null;
    }

    public static string? FindMcpExecutable()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var name in new[] { "AiProject.Console.Mcp.exe", "AiProject.Console.Mcp.dll" })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    public static (string Command, string[] Args) ResolveLaunch(string? workspaceRoot = null)
    {
        var rootArg = string.IsNullOrWhiteSpace(workspaceRoot) ? "${workspaceFolder}" : workspaceRoot;
        var exe = FindMcpExecutable();
        if (exe is not null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (exe, ["--root", rootArg]);
        if (exe is not null)
            return ("dotnet", [exe, "--root", rootArg]);
        var proj = FindMcpProject();
        if (proj is not null)
            return ("dotnet", ["run", "--project", proj, "--", "--root", rootArg]);
        return ("dotnet", ["run", "--project", "src/AiProject.Console.Mcp", "--", "--root", rootArg]);
    }

    public static string CursorSnippet(string? workspaceRoot = null)
    {
        var (command, args) = ResolveLaunch(workspaceRoot);
        var argsArr = new JsonArray();
        foreach (var a in args)
            argsArr.Add(a);
        var root = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [ServerId] = new JsonObject
                {
                    ["command"] = command,
                    ["args"] = argsArr,
                },
            },
        };
        return root.ToJsonString(JsonUtil.Options);
    }

    public static IReadOnlyList<ProjectMcpServer> ListReferenced(string? projectRoot)
    {
        var linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var obj = JsonUtil.LoadObject(CursorConfigPath(projectRoot));
            if (obj["mcpServers"] is JsonObject servers)
            {
                foreach (var kv in servers)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                        linked.Add(kv.Key.Trim());
                }
            }
        }

        var result = new List<ProjectMcpServer>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in linked.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            var meta = SuggestedServers.FirstOrDefault(s =>
                string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            var hasMeta = !string.IsNullOrEmpty(meta.Id);
            result.Add(new ProjectMcpServer(
                id,
                hasMeta ? meta.Title : id,
                hasMeta ? meta.Hint : "專案 MCP",
                Linked: true,
                Ours: string.Equals(id, ServerId, StringComparison.OrdinalIgnoreCase),
                Suggested: hasMeta));
            seen.Add(id);
        }

        result = result
            .OrderByDescending(s => s.Ours)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var s in SuggestedServers)
        {
            if (!seen.Add(s.Id))
                continue;
            result.Add(new ProjectMcpServer(
                s.Id,
                s.Title,
                s.Hint,
                Linked: false,
                Ours: string.Equals(s.Id, ServerId, StringComparison.OrdinalIgnoreCase),
                Suggested: true));
        }

        return result;
    }

    public static bool IsLinked(string? projectRoot, string serverId) =>
        ListReferenced(projectRoot).Any(s =>
            s.Linked && string.Equals(s.Id, serverId, StringComparison.OrdinalIgnoreCase));

    public static string WriteCursorConfig(string projectRoot, string? snippet = null)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var path = CursorConfigPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var incoming = JsonNode.Parse(snippet ?? CursorSnippet(projectRoot)) as JsonObject ?? new JsonObject();
        var existing = JsonUtil.LoadObject(path);
        if (existing["mcpServers"] is JsonObject have && incoming["mcpServers"] is JsonObject add)
        {
            foreach (var kv in add)
                have[kv.Key] = kv.Value?.DeepClone();
            incoming["mcpServers"] = have;
        }
        else if (existing.Count > 0 && incoming["mcpServers"] is not null)
        {
            existing["mcpServers"] = incoming["mcpServers"]?.DeepClone();
            incoming = existing;
        }
        File.WriteAllText(path, incoming.ToJsonString(JsonUtil.Options));
        return path;
    }

    public static string DoctorLine()
    {
        var exe = FindMcpExecutable();
        if (exe is not null)
            return "MCP 伺服器：OK — " + exe;
        var proj = FindMcpProject();
        return proj is null
            ? "MCP 伺服器：可用 `dotnet run --project src/AiProject.Console.Mcp`（設定裡可寫入 .cursor/mcp.json）"
            : "MCP 伺服器：原始碼 — " + proj;
    }

    public static string ResolveRoot(string? cliRoot)
    {
        if (!string.IsNullOrWhiteSpace(cliRoot) && Directory.Exists(cliRoot))
            return Path.GetFullPath(cliRoot);
        var env = Environment.GetEnvironmentVariable("AI_PROJECT_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return Path.GetFullPath(env);
        var last = ConsoleSettingsStore.LastProject();
        if (!string.IsNullOrEmpty(last))
            return last;
        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }
}
