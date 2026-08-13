using System.Diagnostics;
using System.Text;

namespace AiProject.Console.Core.Util;

public static class CliUtil
{
    public static bool CommandExists(string name) =>
        !string.IsNullOrWhiteSpace(FindOnPath(name));

    public static string? FindOnPath(string name)
    {
        if (Path.IsPathRooted(name) && File.Exists(name))
            return name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var ext = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';')
            : [""];
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            foreach (var e in ext)
            {
                var candidate = Path.Combine(dir, name + e);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    public static async Task<(int Code, string Output)> RunAsync(
        string fileName,
        IEnumerable<string> args,
        string? cwd = null,
        int timeoutMs = 120_000,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi };
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (1, sb.ToString().Trim() + "\n（逾時）");
            }
            return (proc.ExitCode, sb.ToString().Trim());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return (1, ex.Message);
        }
    }

    public static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
}
