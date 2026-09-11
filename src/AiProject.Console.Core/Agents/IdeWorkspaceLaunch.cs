using System.Diagnostics;

namespace AiProject.Console.Core.Agents;

/// <summary>
/// 以 IDE CLI 開啟／聚焦工作區。不加 <c>--reuse-window</c>，避免把別的方案視窗換成目前資料夾。
/// </summary>
internal static class IdeWorkspaceLaunch
{
    internal static IReadOnlyList<string> OpenArgs(string path, bool reuseWindow = false, IEnumerable<string>? extraPaths = null)
    {
        var target = Path.GetFullPath(path);
        var folder = Directory.Exists(target) ? target : Path.GetDirectoryName(target)!;
        var args = new List<string>();
        if (reuseWindow)
            args.Add("--reuse-window");
        args.Add(folder);
        if (extraPaths is not null)
        {
            foreach (var extra in extraPaths)
            {
                if (File.Exists(extra))
                    args.Add(Path.GetFullPath(extra));
            }
        }
        return args;
    }

    internal static string? OpenFolder(
        string cli,
        string root,
        bool reuseWindow = false,
        IEnumerable<string>? extraPaths = null,
        string? displayName = null)
    {
        var target = Path.GetFullPath(root);
        if (!File.Exists(target) && !Directory.Exists(target))
            return $"路徑不存在：{target}";
        var psi = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in OpenArgs(target, reuseWindow, extraPaths))
            psi.ArgumentList.Add(arg);
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(cli) : displayName;
            return $"無法啟動 {name}：{ex.Message}";
        }
        return null;
    }
}
