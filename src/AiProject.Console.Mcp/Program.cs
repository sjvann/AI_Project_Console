using AiProject.Console.Core.Stack;
using AiProject.Console.Mcp;

var root = ParseArg(args, "--root") ?? ParseArg(args, "-r");
var invoke = ParseArg(args, "--invoke") ?? ParseArg(args, "-i");

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine("""
        AI_Project 控制台 MCP
          --root <dir>     專案根（預設：AI_PROJECT_ROOT 或上次開啟的專案）
          --invoke <tool>  單次呼叫工具後結束（除錯／腳本）
          --arg name=value  傳給 --invoke 的參數，可重複
          --list-tools     列出工具
        """);
    return 0;
}

if (args.Contains("--list-tools"))
{
    foreach (var t in StackToolRouter.Tools)
        Console.WriteLine($"{t.Name}\t{t.Description}");
    return 0;
}

StackWorkspace workspace;
try
{
    workspace = StackWorkspace.Open(McpLaunch.ResolveRoot(root));
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

if (!string.IsNullOrEmpty(invoke))
{
    var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is not ("--arg" or "-a") || i + 1 >= args.Length)
            continue;
        var pair = args[++i];
        var eq = pair.IndexOf('=');
        if (eq <= 0)
            continue;
        map[pair[..eq]] = pair[(eq + 1)..];
    }
    Console.WriteLine(await StackToolRouter.InvokeAsync(workspace, invoke, map).ConfigureAwait(false));
    return 0;
}

await StdioMcpServer.RunAsync(workspace).ConfigureAwait(false);
return 0;

static string? ParseArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    }
    return null;
}
