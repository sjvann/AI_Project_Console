using System.Diagnostics;
using System.Runtime.InteropServices;

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
        string? windowTitlePrefix = null,
        string? workspaceRoot = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(workspaceRoot))
            {
                CloseWorkspaceWindows(processNames, macAppNames, workspaceRoot);
                return null;
            }

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

    static void CloseWorkspaceWindows(
        IReadOnlyList<string> processNames,
        IReadOnlyList<string>? macAppNames,
        string workspaceRoot)
    {
        if (OperatingSystem.IsWindows())
        {
            CloseMatchingWindowsWindows(processNames, workspaceRoot);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var apps = macAppNames is { Count: > 0 }
                ? macAppNames
                : processNames;
            foreach (var app in apps)
            {
                if (!string.IsNullOrWhiteSpace(app))
                    CloseMatchingWindowsMac(app.Trim(), workspaceRoot);
            }
            return;
        }

        CloseMatchingWindowsLinux(workspaceRoot);
    }

    static void CloseMatchingWindowsWindows(IReadOnlyList<string> processNames, string workspaceRoot)
    {
        var allowed = new HashSet<string>(
            processNames.Select(NormalizeProcessName).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        foreach (var hwnd in Native.TopLevelWindows())
        {
            if (!Native.IsWindowVisible(hwnd))
                continue;
            var title = Native.GetTitle(hwnd);
            if (string.IsNullOrEmpty(title) || !WorkspaceWindow.TitleMatches(title, workspaceRoot))
                continue;
            if (allowed.Count > 0)
            {
                Native.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                    continue;
                try
                {
                    using var proc = Process.GetProcessById(unchecked((int)pid));
                    if (!allowed.Contains(proc.ProcessName))
                        continue;
                }
                catch
                {
                    continue;
                }
            }
            Native.PostMessage(hwnd, Native.WmClose, IntPtr.Zero, IntPtr.Zero);
        }
    }

    static void CloseMatchingWindowsMac(string app, string workspaceRoot)
    {
        var name = WorkspaceWindow.FolderName(workspaceRoot);
        if (string.IsNullOrEmpty(name))
            return;
        var needle = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var appName = app.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var script =
            "tell application \"System Events\"\n" +
            "  if not (exists process \"" + appName + "\") then return\n" +
            "  tell process \"" + appName + "\"\n" +
            "    repeat with w in (get windows)\n" +
            "      set wName to name of w as text\n" +
            "      if (wName contains \" - " + needle + " - \") or (wName starts with \"" + needle + " - \") then\n" +
            "        try\n" +
            "          click (first button of w whose subrole is \"AXCloseButton\")\n" +
            "        end try\n" +
            "      end if\n" +
            "    end repeat\n" +
            "  end tell\n" +
            "end tell\n";
        RunAppleScript(script);
    }

    static void RunAppleScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return;
            p.StandardInput.Write(script);
            p.StandardInput.Close();
            p.WaitForExit(15_000);
        }
        catch
        {
            // ignore
        }
    }

    static void CloseMatchingWindowsLinux(string workspaceRoot)
    {
        try
        {
            using var list = Process.Start(new ProcessStartInfo("wmctrl", "-l")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (list is null)
                return;
            var output = list.StandardOutput.ReadToEnd();
            list.WaitForExit(8_000);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Trim().Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4)
                    continue;
                if (!WorkspaceWindow.TitleMatches(parts[3], workspaceRoot))
                    continue;
                RunSilent("wmctrl", "-ic " + parts[0], 5_000);
            }
        }
        catch
        {
            // 沒有 wmctrl 就不關整份 IDE
        }
    }

    static class Native
    {
        internal const uint WmClose = 0x0010;

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        internal static List<IntPtr> TopLevelWindows()
        {
            var list = new List<IntPtr>();
            EnumWindows((hWnd, _) =>
            {
                list.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        internal static string GetTitle(IntPtr hWnd)
        {
            var len = GetWindowTextLength(hWnd);
            if (len <= 0)
                return "";
            var sb = new System.Text.StringBuilder(len + 1);
            _ = GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
