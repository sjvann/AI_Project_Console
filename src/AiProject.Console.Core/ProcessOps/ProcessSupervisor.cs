using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Cursor;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.ProcessOps;

public static class ProcessSupervisor
{
    private static readonly ConcurrentDictionary<string, ServiceLogWriter> LogWriters = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<bool> HttpOkAsync(string url, int timeoutMs = 2000)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            using var resp = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
            var code = (int)resp.StatusCode;
            return code is >= 200 and < 500;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<bool> ProbeHealthAsync(ServiceEntry svc)
    {
        var url = svc.Health;
        if (string.IsNullOrEmpty(url))
            return svc.Port is not null && await HttpOkAsync($"http://127.0.0.1:{svc.Port}/").ConfigureAwait(false);
        if (await HttpOkAsync(url).ConfigureAwait(false))
            return true;
        if (url.TrimEnd('/').EndsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            var root = url.TrimEnd('/')[..^"/health".Length];
            if (!root.EndsWith('/'))
                root += "/";
            return await HttpOkAsync(root).ConfigureAwait(false);
        }
        return false;
    }

    public static bool PidAlive(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void KillPidTree(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    Process.Start(new ProcessStartInfo("taskkill", $"/F /T /PID {pid}")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                    })?.WaitForExit(10_000);
                }
                catch { /* ignore */ }
            }
        }
    }

    public static void WritePid(ProjectRuntime rt, string stem, int pid) =>
        File.WriteAllText(rt.PidPath(stem), pid + "\n", Encoding.UTF8);

    public static void ClearPid(ProjectRuntime rt, string stem)
    {
        var path = rt.PidPath(stem);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static bool KillPidFile(ProjectRuntime rt, string stem)
    {
        var path = rt.PidPath(stem);
        if (!File.Exists(path))
            return false;
        int pid;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8).Trim().Split()[0];
            pid = int.Parse(text);
        }
        catch (Exception)
        {
            ClearPid(rt, stem);
            return false;
        }
        var killed = false;
        if (PidAlive(pid))
        {
            KillPidTree(pid);
            killed = true;
        }
        ClearPid(rt, stem);
        return killed;
    }

    public static void KillAllPids(ProjectRuntime rt)
    {
        rt.Ensure();
        foreach (var path in Directory.GetFiles(rt.Pids, "*.pid").OrderBy(p => p))
            KillPidFile(rt, Path.GetFileNameWithoutExtension(path));
    }

    public static void ReleasePort(int? port)
    {
        if (port is null or 0)
            return;
        foreach (var pid in PidsOnPort(port.Value))
            KillPidTree(pid);
    }

    public static IReadOnlySet<int> PidsOnPort(int port)
    {
        var pids = new HashSet<int>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano -p tcp")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc is null)
                    return pids;
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(15_000);
                var needle = $":{port} ";
                foreach (var line in output.Split('\n'))
                {
                    if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase) || !line.Contains(needle))
                        continue;
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                        pids.Add(pid);
                }
            }
            catch { /* ignore */ }
            return pids;
        }
        try
        {
            var psi = new ProcessStartInfo("lsof", $"-ti tcp:{port}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null)
                return pids;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);
            foreach (var part in output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var pid))
                    pids.Add(pid);
            }
        }
        catch { /* ignore */ }
        return pids;
    }

    public static string ProjectPathFor(ProjectCatalog catalog, ServiceEntry svc)
    {
        var rel = svc.Project.Replace('\\', '/');
        var path = Path.Combine(catalog.Root, rel.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(path))
        {
            var csprojs = Directory.GetFiles(path, "*.csproj").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return csprojs.Length > 0 ? csprojs[0] : path;
        }
        if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = path + ".csproj";
            if (File.Exists(candidate))
                return candidate;
        }
        return path;
    }

    public static int? StartService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        var stem = host.Stem;
        KillPidFile(rt, stem);
        var proj = ProjectPathFor(catalog, host);
        if (!File.Exists(proj) && !Directory.Exists(proj))
            throw new FileNotFoundException($"找不到專案：{proj}");

        var logFile = rt.LogPath(stem);
        var relProj = Path.GetRelativePath(catalog.Root, Path.GetFullPath(proj));
        var writer = OpenLog(logFile);
        writer.WriteLine($"=== {host.Label} start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = catalog.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(relProj);
        psi.ArgumentList.Add("--no-launch-profile");
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        if (!string.IsNullOrEmpty(host.AspnetUrls))
            psi.Environment["ASPNETCORE_URLS"] = host.AspnetUrls;

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => WriteLog(writer, e.Data);
        proc.ErrorDataReceived += (_, e) => WriteLog(writer, e.Data);
        proc.Exited += (_, _) => CloseLog(logFile, writer);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        WritePid(rt, stem, proc.Id);
        return proc.Id;
    }

    public static void StopService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        KillPidFile(rt, host.Stem);
        ReleasePort(host.Port);
        CloseLog(rt.LogPath(host.Stem));
    }

    public static int? RestartService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        StopService(catalog, rt, host);
        Thread.Sleep(800);
        return StartService(catalog, rt, host);
    }

    public static IReadOnlyList<string> StartAll(ProjectCatalog catalog, ProjectRuntime rt, int delayMs = 500)
    {
        var started = new List<string>();
        foreach (var svc in ServiceCatalogBuilder.OrderedRunnable(catalog))
        {
            StartService(catalog, rt, svc);
            started.Add(svc.Id);
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
        return started;
    }

    public static void StopAll(ProjectCatalog catalog, ProjectRuntime rt)
    {
        foreach (var svc in catalog.Services)
        {
            if (svc.HostedBy is not null)
                continue;
            KillPidFile(rt, svc.Stem);
            ReleasePort(svc.Port);
            CloseLog(rt.LogPath(svc.Stem));
        }
        KillAllPids(rt);
    }

    public static string DoctorReport(ProjectCatalog? catalog)
    {
        var lines = new List<string>
        {
            "AI_Project 環境體檢",
            "",
            $".NET: {Environment.Version}",
            $"控制台: {AppInfo.Version}",
            $"dotnet: {(CliUtil.CommandExists("dotnet") ? "OK" : "缺少")}",
            $"git: {(CliUtil.CommandExists("git") ? "OK" : "缺少")}",
            $"gh: {(CliUtil.CommandExists("gh") ? "OK" : "缺少（GitHub CLI，選用）")}",
        };
        var cursor = CursorLauncher.ResolveCli();
        lines.Add(cursor is null
            ? "Cursor CLI: 缺少（選用；安裝 Cursor 後可自動開專案／求救）"
            : "Cursor CLI: OK — " + cursor);
        if (catalog is null)
        {
            lines.Add("");
            lines.Add("尚未選擇專案目錄。");
            return string.Join('\n', lines);
        }
        lines.AddRange(
        [
            "",
            $"專案：{catalog.Name}",
            $"路徑：{catalog.Root}",
            $"摘要：{catalog.Summary}",
            $"服務：{catalog.Services.Count}",
        ]);
        foreach (var svc in catalog.Services)
        {
            var port = svc.Port?.ToString() ?? "-";
            lines.Add($"  - {svc.Label} [{svc.Id}] port={port} ({svc.Source})");
        }
        lines.Add(catalog.Manifest.Count > 0 ? "manifest: ai-project.json 已載入" : "manifest: 無（使用掃描結果）");
        return string.Join('\n', lines);
    }

    private static ServiceLogWriter OpenLog(string logFile)
    {
        var writer = new ServiceLogWriter(logFile);
        if (LogWriters.TryRemove(logFile, out var previous))
            previous.Dispose();
        LogWriters[logFile] = writer;
        return writer;
    }

    private static void CloseLog(string logFile, ServiceLogWriter? writer = null)
    {
        if (writer is null)
        {
            if (LogWriters.TryRemove(logFile, out var current))
                current.Dispose();
            return;
        }
        LogWriters.TryRemove(new KeyValuePair<string, ServiceLogWriter>(logFile, writer));
        writer.Dispose();
    }

    private static void WriteLog(ServiceLogWriter writer, string? line)
    {
        if (line is null)
            return;
        try
        {
            writer.WriteLine(line);
        }
        catch
        {
            // OutputDataReceived / ErrorDataReceived must never crash the host.
        }
    }

    private sealed class ServiceLogWriter : IDisposable
    {
        private readonly object _lock = new();
        private readonly StreamWriter _writer;
        private bool _disposed;

        public ServiceLogWriter(string path)
        {
            var stream = LogFileUtil.OpenAppend(path);
            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }

        public void WriteLine(string line)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                try
                {
                    _writer.WriteLine(line);
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                try { _writer.Dispose(); }
                catch { /* ignore */ }
            }
        }
    }
}
