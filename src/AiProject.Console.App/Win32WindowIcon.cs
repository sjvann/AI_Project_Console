using System.Runtime.InteropServices;
using Photino.NET;
using AiProject.Console.Core;

namespace AiProject.Console.App;

internal static class Win32WindowIcon
{
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclpHIcon = -14;
    private const int GclpHIconSm = -34;

    public static void BindProcessIdentity()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(AppInfo.AppUserModelId);
        }
        catch
        {
            // ignore: older shells
        }
    }

    public static void Apply(PhotinoWindow window, string? iconFile)
    {
        if (!OperatingSystem.IsWindows())
            return;

        IntPtr hwnd;
        try
        {
            hwnd = window.WindowHandle;
        }
        catch
        {
            return;
        }

        if (hwnd == IntPtr.Zero)
            return;

        if (!TryExtractIcons(iconFile, out var big, out var small))
            return;

        _ = SendMessage(hwnd, WmSetIcon, (IntPtr)IconBig, big);
        _ = SendMessage(hwnd, WmSetIcon, (IntPtr)IconSmall, small);
        _ = SetClassLongPtr(hwnd, GclpHIcon, big);
        _ = SetClassLongPtr(hwnd, GclpHIconSm, small);
    }

    private static bool TryExtractIcons(string? iconFile, out IntPtr big, out IntPtr small)
    {
        big = IntPtr.Zero;
        small = IntPtr.Zero;

        foreach (var path in IconCandidates(iconFile))
        {
            if (ExtractIconEx(path, 0, out big, out small, 1) > 0 &&
                big != IntPtr.Zero && small != IntPtr.Zero)
                return true;

            if (big != IntPtr.Zero) { _ = DestroyIcon(big); big = IntPtr.Zero; }
            if (small != IntPtr.Zero) { _ = DestroyIcon(small); small = IntPtr.Zero; }
        }

        return false;
    }

    private static IEnumerable<string> IconCandidates(string? iconFile)
    {
        if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile))
            yield return iconFile;

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
            yield return exe;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern IntPtr SetClassLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetClassLongPtr64(hWnd, nIndex, dwNewLong)
            : SetClassLong32(hWnd, nIndex, dwNewLong);
}
