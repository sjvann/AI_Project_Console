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
        CancellationToken ct = default,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        Action<string>? onLine = null)
    {
        var (code, stdout, stderr) = await RunCaptureAsync(
            fileName, args, cwd, timeoutMs, ct, stdin: stdin, extraEnv: extraEnv, onLine: onLine).ConfigureAwait(false);
        var output = string.Join('\n', new[] { stdout, stderr }.Where(s => !string.IsNullOrEmpty(s))).Trim();
        return (code, output);
    }

    public static async Task<(int Code, string StdOut, string StdErr)> RunCaptureAsync(
        string fileName,
        IEnumerable<string> args,
        string? cwd = null,
        int timeoutMs = 120_000,
        CancellationToken ct = default,
        bool trim = true,
        string? stdin = null,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        Action<string>? onLine = null)
    {
        var redirectIn = stdin is not null;
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectIn,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (extraEnv is not null)
        {
            foreach (var kv in extraEnv)
                psi.Environment[kv.Key] = kv.Value;
        }
        if (redirectIn)
            psi.StandardInputEncoding = Encoding.UTF8;
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var proc = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            void Append(StringBuilder sb, string? data)
            {
                if (data is null)
                    return;
                sb.AppendLine(data);
                if (onLine is null)
                    return;
                try { onLine(data); }
                catch { /* 呼叫端不應讓擷取中斷 */ }
            }
            proc.OutputDataReceived += (_, e) => Append(stdout, e.Data);
            proc.ErrorDataReceived += (_, e) => Append(stderr, e.Data);
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            if (redirectIn)
            {
                await proc.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                if (stdin!.Length == 0 || stdin[^1] != '\n')
                    await proc.StandardInput.WriteAsync('\n').ConfigureAwait(false);
                proc.StandardInput.Close();
            }
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                if (!ct.IsCancellationRequested)
                {
                    stderr.AppendLine("（逾時）");
                    return (1, FinishCaptured(stdout, trim), FinishCaptured(stderr, trim));
                }
                throw;
            }
            return (proc.ExitCode, FinishCaptured(stdout, trim), FinishCaptured(stderr, trim));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return (1, "", ex.Message);
        }
    }

    /// <summary>
    /// porcelain 等格式會用開頭空白當欄位；不可 TrimStart。
    /// </summary>
    private static string FinishCaptured(StringBuilder sb, bool trim)
    {
        var text = sb.ToString();
        return trim ? text.Trim() : text.TrimEnd('\r', '\n');
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
