using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AiProject.Console.Core.Update;

internal static class DetachedProcess
{
    private const uint CreateBreakawayFromJob = 0x01000000;
    private const uint CreateNewProcessGroup = 0x00000200;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const int StartfUseShowWindow = 0x00000001;
    private const short SwHide = 0;
    private const short SwShownormal = 1;

    public static int Start(string fileName, string arguments, bool showWindow, string? workingDirectory = null)
    {
        var exe = Path.GetFullPath(fileName);
        if (!File.Exists(exe))
            throw new InvalidOperationException("找不到檔案：" + exe);

        var line = new StringBuilder(exe.Length + (arguments?.Length ?? 0) + 8);
        line.Append('"').Append(exe).Append('"');
        if (!string.IsNullOrWhiteSpace(arguments))
            line.Append(' ').Append(arguments);

        var cwd = string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetDirectoryName(exe)
            : Path.GetFullPath(workingDirectory);

        var flags = CreateNewProcessGroup | CreateUnicodeEnvironment
            | (showWindow ? 0 : CreateNoWindow);
        var pid = TryCreate(exe, new StringBuilder(line.ToString()), cwd, flags | CreateBreakawayFromJob, showWindow);
        if (pid > 0)
            return pid;
        pid = TryCreate(exe, new StringBuilder(line.ToString()), cwd, flags, showWindow);
        if (pid > 0)
            return pid;
        throw new Win32Exception(Marshal.GetLastWin32Error(), "無法獨立啟動：" + exe);
    }

    static int TryCreate(string exe, StringBuilder commandLine, string? cwd, uint flags, bool showWindow)
    {
        var si = new StartupInfo
        {
            cb = Marshal.SizeOf<StartupInfo>(),
            dwFlags = StartfUseShowWindow,
            wShowWindow = showWindow ? SwShownormal : SwHide,
        };
        if (!CreateProcessW(
                exe,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                IntPtr.Zero,
                cwd,
                ref si,
                out var pi))
            return 0;
        if (pi.hThread != IntPtr.Zero)
            CloseHandle(pi.hThread);
        if (pi.hProcess != IntPtr.Zero)
            CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}
