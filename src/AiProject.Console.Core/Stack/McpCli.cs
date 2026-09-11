namespace AiProject.Console.Core.Stack;

/// <summary>
/// 控制台產品自己的 MCP 進入點。桌面 exe 加 <c>--mcp</c>，或獨立 <c>AiProject.Console.Mcp</c> 專案皆走這裡。
/// </summary>
public static class McpCli
{
    public const string Flag = "--mcp";

    public static bool IsRequested(IEnumerable<string>? args) =>
        args is not null && args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args) =>
        RunAsync(args).GetAwaiter().GetResult();

    public static async Task<int> RunAsync(string[] args)
    {
        args = args.Where(a => !string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)).ToArray();
        var root = ParseArg(args, "--root") ?? ParseArg(args, "-r");
        var invoke = ParseArg(args, "--invoke") ?? ParseArg(args, "-i");

        if (args.Contains("--help") || args.Contains("-h"))
        {
            System.Console.Error.WriteLine("""
                AI_Project 控制台 MCP
                  --mcp            從控制台 exe／dll 進入 MCP（Cursor mcp.json 用）
                  --root <dir>     受管理的專案根（預設：AI_PROJECT_ROOT 或上次開啟的專案）
                  --invoke <tool>  單次呼叫工具後結束（除錯／腳本）
                  --arg name=value  傳給 --invoke 的參數，可重複
                  --list-tools     列出目前政策允許的工具
                """);
            return 0;
        }

        if (args.Contains("--list-tools"))
        {
            try
            {
                var ws = StackWorkspace.Open(McpLaunch.ResolveRoot(root));
                var policy = McpPolicy.Load(ws.Root);
                foreach (var t in StackToolRouter.VisibleTools(policy))
                    System.Console.WriteLine($"{t.Name}\t{t.Description}");
            }
            catch
            {
                foreach (var t in StackToolRouter.Tools)
                    System.Console.WriteLine($"{t.Name}\t{t.Description}");
            }
            return 0;
        }

        StackWorkspace workspace;
        try
        {
            workspace = StackWorkspace.Open(McpLaunch.ResolveRoot(root));
        }
        catch (Exception ex)
        {
            System.Console.Error.WriteLine(ex.Message);
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
            System.Console.WriteLine(await StackToolRouter.InvokeAsync(workspace, invoke, map).ConfigureAwait(false));
            return 0;
        }

        await StdioMcpServer.RunAsync(workspace).ConfigureAwait(false);
        return 0;
    }

    static string? ParseArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
