using System.Diagnostics;
using System.Text;

namespace AiProject.Console.Core.Agents;

public static class TerminalSession
{
    public static string? OpenUri(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
            return null;
        }
        catch (Exception ex)
        {
            return $"無法開啟連結：{ex.Message}";
        }
    }

    public static string? Start(string fileName, IReadOnlyList<string> args, string cwd, string title)
    {
        try
        {
            cwd = Path.GetFullPath(cwd);
            if (OperatingSystem.IsWindows())
            {
                var script = Path.Combine(Path.GetTempPath(), $"ai-agent-{Guid.NewGuid():N}.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("cd /d \"" + cwd.Replace("\"", "") + "\"");
                sb.Append('"' + fileName + '"');
                foreach (var a in args)
                    sb.Append(' ').Append(QuoteWin(a));
                sb.AppendLine();
                File.WriteAllText(script, sb.ToString(), Encoding.Default);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c start \"" + title.Replace("\"", "") + "\" \"" + script + "\"",
                    UseShellExecute = true,
                });
                return null;
            }

            if (OperatingSystem.IsMacOS())
            {
                var cmd = string.Join(" ", new[] { fileName }.Concat(args).Select(QuoteSh));
                var script = $"cd {QuoteSh(cwd)} && {cmd}";
                Process.Start(new ProcessStartInfo("osascript",
                    "-e \"tell application \\\"Terminal\\\" to do script " + QuoteApple(script) + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return null;
            }

            var linuxCmd = string.Join(" ", new[] { fileName }.Concat(args).Select(QuoteSh));
            Process.Start(new ProcessStartInfo("x-terminal-emulator", $"-e bash -lc {QuoteSh("cd " + QuoteSh(cwd) + " && " + linuxCmd)}")
            {
                UseShellExecute = false,
            });
            return null;
        }
        catch (Exception ex)
        {
            return $"無法開啟終端機：{ex.Message}";
        }
    }

    public static string Expand(string template, string root, string prompt, string promptFile)
    {
        return (template ?? "")
            .Replace("{root}", root, StringComparison.OrdinalIgnoreCase)
            .Replace("{promptFile}", promptFile, StringComparison.OrdinalIgnoreCase)
            .Replace("{prompt}", prompt, StringComparison.OrdinalIgnoreCase);
    }

    static string QuoteWin(string value)
    {
        if (value.IndexOfAny([' ', '"']) < 0)
            return value;
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    static string QuoteSh(string value) => "'" + value.Replace("'", "'\\''") + "'";

    static string QuoteApple(string value) => "\\\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\\\"";
}
