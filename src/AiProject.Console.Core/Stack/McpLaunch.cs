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

public sealed record McpCatalogEntry(
    string Id,
    string Title,
    string Hint,
    string Kind,
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    string? Url = null,
    string? AfterAddHint = null)
{
    public bool Ours => string.Equals(Id, McpLaunch.ServerId, StringComparison.OrdinalIgnoreCase);

    public JsonObject? DefaultConfig()
    {
        if (Ours)
            return null;
        if (string.Equals(Kind, "url", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Url))
            return new JsonObject { ["url"] = Url };
        if (string.IsNullOrWhiteSpace(Command))
            return null;
        var obj = new JsonObject { ["command"] = Command };
        if (Args is { Count: > 0 })
        {
            var arr = new JsonArray();
            foreach (var a in Args)
                arr.Add(a);
            obj["args"] = arr;
        }
        return obj;
    }
}

public static class McpLaunch
{
    public const string ServerId = "ai-project-console";
    public const string CustomPickerId = "__custom__";

    public static readonly IReadOnlyList<McpCatalogEntry> Catalog =
    [
        new(ServerId, "本控制台", "堆疊、編譯、啟停、Log", "ours"),
        new("github", "GitHub", "PR／Issue／Actions", "url",
            Url: "https://api.githubcopilot.com/mcp/",
            AfterAddHint: "已寫入遠端 GitHub MCP。請到 Cursor 設定或編輯 mcp.json，補上 Personal Access Token。"),
        new("context7", "Context7", "套件文件", "command",
            Command: "npx",
            Args: ["-y", "@upstash/context7-mcp"],
            AfterAddHint: "已加入 Context7（npx）。重新載入 Cursor 後即可查套件文件。"),
    ];

    public static IReadOnlyList<(string Id, string Title, string Hint)> SuggestedServers =>
        Catalog.Select(c => (c.Id, c.Title, c.Hint)).ToArray();

    public static McpCatalogEntry? FindCatalog(string? id) =>
        Catalog.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidServerId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        id = id.Trim();
        if (string.Equals(id, CustomPickerId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!char.IsAsciiLetterOrDigit(id[0]))
            return false;
        foreach (var ch in id)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
                return false;
        }
        return true;
    }

    public static IReadOnlyList<string> SplitArgs(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string CursorConfigPath(string projectRoot) =>
        Path.Combine(Path.GetFullPath(projectRoot), ".cursor", "mcp.json");

    /// <summary>
    /// 目前這份控制台產品的目錄（exe／dll），不是受管理專案的 <c>src</c>。
    /// </summary>
    public static string ProductDirectory() => Path.GetFullPath(AppContext.BaseDirectory);

    public static string? FindProductHost()
    {
        var dir = ProductDirectory();
        var exe = Path.Combine(dir, AppInfo.ExeName);
        if (File.Exists(exe))
            return exe;
        var dll = Path.Combine(dir, Path.ChangeExtension(AppInfo.ExeName, ".dll"));
        if (File.Exists(dll))
            return dll;
        return null;
    }

    public static string? FindMcpProject()
    {
        // 只從控制台產品目錄往上找本倉原始碼，不從「目前目錄／受管理專案」找，
        // 否則相對路徑 src/AiProject.Console.Mcp 會變成對方方案的 src。
        var dir = new DirectoryInfo(ProductDirectory());
        while (dir is not null)
        {
            var proj = Path.Combine(dir.FullName, "src", "AiProject.Console.Mcp", "AiProject.Console.Mcp.csproj");
            if (File.Exists(proj))
                return Path.GetFullPath(proj);
            dir = dir.Parent;
        }
        return null;
    }

    public static string? FindMcpExecutable()
    {
        foreach (var dir in ProductSearchDirs())
        {
            foreach (var name in new[] { "AiProject.Console.Mcp.exe", "AiProject.Console.Mcp.dll" })
            {
                var path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
        }
        return null;
    }

    static IEnumerable<string> ProductSearchDirs()
    {
        var baseDir = ProductDirectory();
        yield return baseDir;
        var sibling = baseDir.Replace("AiProject.Console.App", "AiProject.Console.Mcp", StringComparison.OrdinalIgnoreCase);
        if (!string.Equals(sibling, baseDir, StringComparison.OrdinalIgnoreCase))
            yield return sibling;
    }

    public static (string Command, string[] Args) ResolveLaunch(string? workspaceRoot = null)
    {
        var rootArg = string.IsNullOrWhiteSpace(workspaceRoot)
            ? "${workspaceFolder}"
            : Path.GetFullPath(workspaceRoot);

        var host = FindProductHost();
        if (host is not null && host.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (host, [McpCli.Flag, "--root", rootArg]);
        if (host is not null)
            return ("dotnet", [host, McpCli.Flag, "--root", rootArg]);

        var exe = FindMcpExecutable();
        if (exe is not null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (exe, ["--root", rootArg]);
        if (exe is not null)
            return ("dotnet", [exe, "--root", rootArg]);

        var proj = FindMcpProject();
        if (proj is not null)
            return ("dotnet", ["run", "--project", proj, "--", "--root", rootArg]);

        throw new InvalidOperationException(
            "找不到 AI_Project 控制台 MCP。請從本控制台寫入 mcp.json，不要指向受管理專案的 src。");
    }

    public static string CursorSnippet(string? workspaceRoot = null)
    {
        var (command, args) = ResolveLaunch(workspaceRoot);
        var argsArr = new JsonArray();
        foreach (var a in args)
            argsArr.Add(a);
        var server = new JsonObject
        {
            ["command"] = command,
            ["args"] = argsArr,
            ["cwd"] = ProductDirectory(),
        };
        var root = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                [ServerId] = server,
            },
        };
        return root.ToJsonString(JsonUtil.Options);
    }

    /// <summary>
    /// 若專案已加入「本控制台」，把 command 改寫成目前這份產品（避免舊的相對 src 路徑）。
    /// </summary>
    public static bool TryRepairOurs(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !IsLinked(projectRoot, ServerId))
            return false;
        WriteCursorConfig(projectRoot);
        return true;
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
        if (existing["mcpServers"] is JsonObject have)
        {
            if (incoming["mcpServers"] is JsonObject add)
            {
                foreach (var kv in add)
                    have[kv.Key] = kv.Value?.DeepClone();
            }
            File.WriteAllText(path, existing.ToJsonString(JsonUtil.Options));
            return path;
        }
        if (existing.Count > 0 && incoming["mcpServers"] is not null)
        {
            existing["mcpServers"] = incoming["mcpServers"]?.DeepClone();
            File.WriteAllText(path, existing.ToJsonString(JsonUtil.Options));
            return path;
        }
        File.WriteAllText(path, incoming.ToJsonString(JsonUtil.Options));
        return path;
    }

    public static string ServerSnippet(string serverId, string? workspaceRoot = null)
    {
        if (string.Equals(serverId, ServerId, StringComparison.OrdinalIgnoreCase))
            return CursorSnippet(workspaceRoot);
        var entry = FindCatalog(serverId)
            ?? throw new ArgumentException("未知的 MCP 範本：" + serverId);
        var config = entry.DefaultConfig()
            ?? throw new InvalidOperationException("此範本沒有可寫入的設定。");
        return new JsonObject
        {
            ["mcpServers"] = new JsonObject { [serverId] = config },
        }.ToJsonString(JsonUtil.Options);
    }

    public static string WriteCatalogServer(string projectRoot, string serverId) =>
        WriteCursorConfig(projectRoot, ServerSnippet(serverId, projectRoot));

    public static string WriteCustomServer(string projectRoot, string serverId, string? command, string? args, string? url)
    {
        if (!IsValidServerId(serverId))
            throw new ArgumentException("伺服器識別名稱只能用英數開頭，並含英數、點、底線、連字號。");
        JsonObject config;
        if (!string.IsNullOrWhiteSpace(url))
        {
            config = new JsonObject { ["url"] = url.Trim() };
        }
        else if (!string.IsNullOrWhiteSpace(command))
        {
            config = new JsonObject { ["command"] = command.Trim() };
            var list = SplitArgs(args);
            if (list.Count > 0)
            {
                var arr = new JsonArray();
                foreach (var a in list)
                    arr.Add(a);
                config["args"] = arr;
            }
        }
        else
        {
            throw new ArgumentException("請填命令，或填遠端 URL。");
        }

        var snippet = new JsonObject
        {
            ["mcpServers"] = new JsonObject { [serverId.Trim()] = config },
        }.ToJsonString(JsonUtil.Options);
        return WriteCursorConfig(projectRoot, snippet);
    }

    public static string RemoveServer(string projectRoot, string serverId)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        var path = CursorConfigPath(projectRoot);
        var existing = JsonUtil.LoadObject(path);
        if (existing["mcpServers"] is JsonObject have)
            have.Remove(serverId);
        File.WriteAllText(path, existing.ToJsonString(JsonUtil.Options));
        return path;
    }

    public static string DoctorLine()
    {
        var host = FindProductHost();
        if (host is not null)
            return "MCP 伺服器：本控制台 — " + host;
        var exe = FindMcpExecutable();
        if (exe is not null)
            return "MCP 伺服器：OK — " + exe;
        var proj = FindMcpProject();
        return proj is null
            ? "MCP 伺服器：請從本控制台寫入 mcp.json（指向 AI_Project 控制台，不是受管理專案的 src）"
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
