using System.Diagnostics;

namespace AiProject.Console.Core.Agents;

/// <summary>
/// 以正常關閉（WM_CLOSE／quit）結束本機 IDE 或終端機，避免強制結束 Electron 渲染行程。
/// </summary>
public static class LocalAppCloser
{
    public static bool IsRunning(params string[] processNames)
    {
        foreach (var name in processNames)
        {
            var key = NormalizeProcessName(name);
            if (string.IsNullOrEmpty(key))
                continue;
            try
            {
                if (Process.GetProcessesByName(key).Length > 0)
                    return true;
            }
            catch
            {
                // ignore
            }
        }
        return false;
    }

    public static bool HasWindowTitle(string titlePrefix)
    {
        if (string.IsNullOrWhiteSpace(titlePrefix))
            return false;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var title = p.MainWindowTitle;
                if (!string.IsNullOrEmpty(title)
                    && title.StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // ignore
            }
            finally
            {
                p.Dispose();
            }
        }
        return false;
    }

    public static string? Close(
        string displayName,
        IReadOnlyList<string> processNames,
        IReadOnlyList<string>? windowsImages = null,
        IReadOnlyList<string>? macAppNames = null,
        string? unixPattern = null,
        string? windowTitlePrefix = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                CloseWindowsGracefully(processNames, windowsImages, windowTitlePrefix);
                return null;
            }

            if (OperatingSystem.IsMacOS())
            {
                if (macAppNames is not null)
                {
                    foreach (var app in macAppNames)
                    {
                        if (string.IsNullOrWhiteSpace(app))
                            continue;
                        Process.Start(new ProcessStartInfo("osascript",
                            "-e \"tell application \\\"" + app.Replace("\"", "") + "\\\" to quit\"")
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        })?.WaitForExit(30_000);
                    }
                }

                if (!string.IsNullOrWhiteSpace(windowTitlePrefix))
                    CloseMacTerminalWindows(windowTitlePrefix);

                if ((macAppNames is null || macAppNames.Count == 0) && !string.IsNullOrWhiteSpace(unixPattern))
                    RunSilent("pkill", "-f " + unixPattern, 15_000);
                return null;
            }

            var pattern = unixPattern ?? processNames.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(pattern))
                RunSilent("pkill", "-f " + pattern, 15_000);
            return null;
        }
        catch (Exception ex)
        {
            return $"關閉 {displayName} 失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// 對主視窗送 WM_CLOSE。不可 Process.Kill／taskkill /F：強制結束 Electron 渲染行程時，
    /// 主行程會跳出「The window terminated unexpectedly」。
    /// </summary>
    static void CloseWindowsGracefully(
        IReadOnlyList<string> processNames,
        IReadOnlyList<string>? windowsImages,
        string? windowTitlePrefix)
    {
        var closedAny = CloseMainWindows(processNames, windowTitlePrefix);
        if (closedAny && WaitUntilExits(processNames, windowTitlePrefix, TimeSpan.FromSeconds(8)))
            return;

        if (windowsImages is not null)
        {
            foreach (var image in windowsImages)
            {
                if (string.IsNullOrWhiteSpace(image))
                    continue;
                RunSilent("taskkill", "/IM " + image.Trim(), 8_000);
            }
        }

        if (!string.IsNullOrWhiteSpace(windowTitlePrefix))
            RunSilent("taskkill", "/FI \"WINDOWTITLE eq " + windowTitlePrefix + "*\"", 8_000);

        WaitUntilExits(processNames, windowTitlePrefix, TimeSpan.FromSeconds(5));
    }

    static bool CloseMainWindows(IReadOnlyList<string> processNames, string? windowTitlePrefix)
    {
        var closedAny = false;
        foreach (var name in processNames)
        {
            var key = NormalizeProcessName(name);
            if (string.IsNullOrEmpty(key))
                continue;
            foreach (var p in Process.GetProcessesByName(key))
                closedAny |= TryCloseMainWindow(p);
        }

        if (string.IsNullOrWhiteSpace(windowTitlePrefix))
            return closedAny;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var title = p.MainWindowTitle;
                if (!string.IsNullOrEmpty(title)
                    && title.StartsWith(windowTitlePrefix, StringComparison.OrdinalIgnoreCase))
                    closedAny |= TryCloseMainWindow(p, dispose: false);
            }
            catch
            {
                // ignore
            }
            finally
            {
                p.Dispose();
            }
        }

        return closedAny;
    }

    static bool TryCloseMainWindow(Process p, bool dispose = true)
    {
        try
        {
            if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
                return p.CloseMainWindow();
        }
        catch
        {
            // ignore
        }
        finally
        {
            if (dispose)
                p.Dispose();
        }
        return false;
    }

    static bool WaitUntilExits(IReadOnlyList<string> processNames, string? windowTitlePrefix, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsOpen(processNames, windowTitlePrefix))
                return true;
            Thread.Sleep(250);
        }
        return !IsOpen(processNames, windowTitlePrefix);
    }

    static bool IsOpen(IReadOnlyList<string> processNames, string? windowTitlePrefix) =>
        IsRunning(processNames.ToArray())
        || (!string.IsNullOrWhiteSpace(windowTitlePrefix) && HasWindowTitle(windowTitlePrefix));

    static void CloseMacTerminalWindows(string titlePrefix)
    {
        var safe = titlePrefix.Replace("\\", "\\\\").Replace("\"", "\\\"");
        RunSilent("osascript",
            "-e \"tell application \\\"Terminal\\\" to close (every window whose name contains \\\"" + safe + "\\\")\"",
            15_000);
    }

    static void RunSilent(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            p?.WaitForExit(timeoutMs);
        }
        catch
        {
            // ignore
        }
    }

    static string NormalizeProcessName(string name)
    {
        var n = (name ?? "").Trim();
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            n = n[..^4];
        return n;
    }
}
