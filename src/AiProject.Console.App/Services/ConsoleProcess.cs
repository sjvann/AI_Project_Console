using System.Diagnostics;
using AiProject.Console.Core.Runtime;

namespace AiProject.Console.App.Services;

/// <summary>
/// 同一使用者可開多個控制台視窗，各自管不同專案。WebView2 使用者資料夾必須每行程一份，
/// 否則後開的視窗會跟先開的共用 session，選專案時兩個視窗一起被改掉。
/// </summary>
internal static class ConsoleProcess
{
    private const string PrimaryMutexName = @"Local\sjvann.AIProjectConsole.primary";
    private static Mutex? _primary;
    private static string? _webViewDir;

    public static bool IsPrimary { get; private set; } = true;

    public static string Register()
    {
        TryClaimPrimary();
        _webViewDir = CreateWebViewDir();
        return _webViewDir;
    }

    public static void Release()
    {
        TryDeleteDir(_webViewDir);
        _webViewDir = null;
        try
        {
            _primary?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 不是擁有者
        }
        _primary?.Dispose();
        _primary = null;
    }

    static void TryClaimPrimary()
    {
        try
        {
            _primary = new Mutex(initiallyOwned: true, PrimaryMutexName, out var created);
            IsPrimary = created;
            if (!created)
            {
                _primary.Dispose();
                _primary = null;
            }
        }
        catch (AbandonedMutexException ex)
        {
            IsPrimary = true;
            _primary = ex.Mutex;
        }
        catch
        {
            IsPrimary = true;
        }
    }

    static string CreateWebViewDir()
    {
        var root = Path.Combine(Path.GetDirectoryName(ConsoleSettingsStore.SettingsPath())!, "webview");
        Directory.CreateDirectory(root);
        PruneStale(root);
        var dir = Path.Combine(root, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void PruneStale(string root)
    {
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(root);
        }
        catch
        {
            return;
        }

        foreach (var dir in dirs)
        {
            var name = Path.GetFileName(dir);
            var dash = name.IndexOf('-');
            if (dash <= 0 || !int.TryParse(name[..dash], out var pid))
                continue;
            if (PidStillOurs(pid))
                continue;
            TryDeleteDir(dir);
        }
    }

    static bool PidStillOurs(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.HasExited)
                return false;
            var n = proc.ProcessName;
            return n.Contains("AI_Project_Console", StringComparison.OrdinalIgnoreCase)
                   || n.Contains("dotnet", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    static void TryDeleteDir(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return;
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // WebView2 可能還鎖著檔；下次啟動再清
        }
    }
}
