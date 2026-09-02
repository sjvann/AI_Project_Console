using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AiProject.Console.Core.Catalog;
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

    public static async Task<bool> ProbeHealthAsync(ProjectCatalog catalog, ServiceEntry svc) =>
        ServiceCatalogBuilder.IsCurrentConsole(catalog, svc) || await ProbeHealthAsync(svc).ConfigureAwait(false);

    public static async Task<bool> ProbeHealthAsync(ServiceEntry svc)
    {
        var url = (svc.Health ?? "").Trim();
        if (url.StartsWith("mutex:", StringComparison.OrdinalIgnoreCase))
            return MutexHeld(url["mutex:".Length..].Trim());
        if (url.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(url["tcp:".Length..].Trim(), out var tcpPort) && TcpOpen(tcpPort);
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

    /// <summary>具名 mutex 已被其他行程持有（Photino 桌面控制台用）。殘留 abandoned 不當成在線。</summary>
    public static bool MutexHeld(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        try
        {
            if (Mutex.TryOpenExisting(name, out var existing))
            {
                existing.Dispose();
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            return false;
        }
        catch
        {
            return false;
        }
        return false;
    }

    public static bool TcpOpen(int port)
    {
        if (port <= 0)
            return false;
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var ar = tcp.BeginConnect("127.0.0.1", port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200)))
                return false;
            tcp.EndConnect(ar);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
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

    /// <summary>
    /// 本控制台曾寫入 pid 檔、但行程已不在。用來清掉卡在「啟動中…」的列
    ///（桌面程式沒 HTTP 時，健康檢查永遠不會變綠）。
    /// </summary>
    public static IReadOnlySet<string> DeadStartedIds(ProjectCatalog catalog, ProjectRuntime rt)
    {
        var dead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in catalog.Services)
        {
            if (svc.HostedBy is not null)
                continue;
            var path = rt.PidPath(svc.Stem);
            if (!File.Exists(path))
                continue;
            if (!TryParsePidFile(path, out var pid) || !PidAlive(pid))
                dead.Add(svc.Id);
        }
        foreach (var svc in catalog.Services)
        {
            if (svc.HostedBy is not null && dead.Contains(svc.HostedBy))
                dead.Add(svc.Id);
        }
        return dead;
    }

    internal static bool TryParsePidFile(string path, out int pid)
    {
        pid = 0;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8).Trim().Split()[0];
            return int.TryParse(text, out pid) && pid > 0;
        }
        catch
        {
            return false;
        }
    }

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
        if (!TryParsePidFile(path, out var pid))
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
        if (File.Exists(path))
            return path;
        if (!path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = path + ".csproj";
            if (File.Exists(candidate))
                return candidate;
        }
        return path;
    }

    public const string SelfConsoleStartMessage = "這是目前這個控制台，再啟動會再開一扇視窗。";

    public static int? StartService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        if (ServiceCatalogBuilder.IsCurrentConsole(catalog, svc))
            throw new InvalidOperationException(SelfConsoleStartMessage);
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        var stem = host.Stem;
        KillPidFile(rt, stem);
        var proj = ProjectPathFor(catalog, host);
        if (!File.Exists(proj) && !Directory.Exists(proj))
            throw new FileNotFoundException($"找不到專案：{proj}");

        var logFile = rt.LogPath(stem);
        var writer = OpenLog(logFile);
        writer.WriteLine($"=== {host.Label} start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        var prep = RunPreStart(catalog, host, writer);
        if (prep is not null)
        {
            writer.WriteLine(prep);
            CloseLog(logFile, writer);
            throw new InvalidOperationException(prep);
        }

        var psi = CreateStartInfo(catalog, host, proj);
        writer.WriteLine($"{psi.FileName} {string.Join(' ', psi.ArgumentList)}");
        writer.WriteLine($"cwd {psi.WorkingDirectory}");

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

    internal static bool IsPythonScript(string path) =>
        path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);

    internal static string ResolvePythonWorkingDirectory(string scriptPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
        if (!string.IsNullOrEmpty(dir)
            && Path.GetFileName(dir).Equals("developer", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parent))
                return parent;
        }
        return dir ?? Environment.CurrentDirectory;
    }

    internal static bool IsPyLauncher(string fileName) =>
        Path.GetFileNameWithoutExtension(fileName).Equals("py", StringComparison.OrdinalIgnoreCase);

    internal static string? ResolvePythonLauncherPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var py = CliUtil.FindOnPath("py");
            if (py is not null)
                return py;
        }
        foreach (var name in new[] { "python", "python3" })
        {
            var found = CliUtil.FindOnPath(name);
            if (found is not null)
                return found;
        }
        return null;
    }

    internal static bool HasPythonLauncher() => ResolvePythonLauncherPath() is not null;

    internal static string ResolvePythonLauncher() =>
        ResolvePythonLauncherPath() ?? (OperatingSystem.IsWindows() ? "py" : "python3");

    internal static bool RequiresDotnet(ProjectCatalog catalog, ServiceEntry svc)
    {
        var path = ProjectPathFor(catalog, ServiceCatalogBuilder.HostService(catalog, svc));
        return !IsPythonScript(path);
    }

    internal static ProcessStartInfo CreateStartInfo(ProjectCatalog catalog, ServiceEntry host, string proj)
    {
        var full = Path.GetFullPath(proj);
        if (IsPythonScript(full))
        {
            var workDir = ResolvePythonWorkingDirectory(full);
            var relScript = Path.GetRelativePath(workDir, full);
            var launcher = ResolvePythonLauncher();
            var python = NewRedirected(launcher, workDir);
            if (IsPyLauncher(launcher))
                python.ArgumentList.Add("-3");
            python.ArgumentList.Add(relScript.Replace('\\', '/'));
            python.Environment["PYTHONUNBUFFERED"] = "1";
            return python;
        }

        var relProj = Path.GetRelativePath(catalog.Root, full);
        var psi = NewRedirected("dotnet", catalog.Root);
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(relProj);
        psi.ArgumentList.Add("--no-launch-profile");
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        if (!string.IsNullOrEmpty(host.AspnetUrls))
            psi.Environment["ASPNETCORE_URLS"] = host.AspnetUrls;
        return psi;
    }

    private static ProcessStartInfo NewRedirected(string fileName, string workDir) => new(fileName)
    {
        WorkingDirectory = workDir,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
    };

    public static string? TryStartService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        try
        {
            var pid = StartService(catalog, rt, svc);
            if (pid is int p)
            {
                Thread.Sleep(600);
                if (!PidAlive(p))
                    return "行程立即結束，請查看 Log";
            }
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public static IReadOnlyList<ServiceEntry> OfflineRunnable(
        ProjectCatalog catalog,
        IReadOnlyDictionary<string, bool> health,
        IReadOnlySet<string>? ids = null)
    {
        var list = new List<ServiceEntry>();
        foreach (var svc in ServiceCatalogBuilder.OrderedRunnable(catalog))
        {
            if (ids is not null && !ids.Contains(svc.Id))
                continue;
            if (ServiceCatalogBuilder.IsCurrentConsole(catalog, svc) || health.GetValueOrDefault(svc.Id))
                continue;
            list.Add(svc);
        }
        return list;
    }

    public static IReadOnlyList<ServiceEntry> OnlineRunnable(
        ProjectCatalog catalog,
        IReadOnlyDictionary<string, bool> health,
        IReadOnlySet<string>? ids = null)
    {
        var list = new List<ServiceEntry>();
        foreach (var svc in ServiceCatalogBuilder.OrderedRunnable(catalog))
        {
            if (ids is not null && !ids.Contains(svc.Id))
                continue;
            if (ServiceCatalogBuilder.IsCurrentConsole(catalog, svc) || !health.GetValueOrDefault(svc.Id))
                continue;
            list.Add(svc);
        }
        return list;
    }

    public static IReadOnlyList<(string Id, string Label, string? Error)> StartOffline(
        ProjectCatalog catalog,
        ProjectRuntime rt,
        IReadOnlyDictionary<string, bool> health,
        IReadOnlySet<string>? ids = null,
        int delayMs = 500)
    {
        var results = new List<(string Id, string Label, string? Error)>();
        foreach (var svc in OfflineRunnable(catalog, health, ids))
        {
            var err = TryStartService(catalog, rt, svc);
            results.Add((svc.Id, svc.Label, err));
            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
        return results;
    }

    public static void StopServices(
        ProjectCatalog catalog,
        ProjectRuntime rt,
        IEnumerable<ServiceEntry> services)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in services)
        {
            if (svc.HostedBy is not null || ServiceCatalogBuilder.IsCurrentConsole(catalog, svc))
                continue;
            if (!seen.Add(svc.Id))
                continue;
            StopService(catalog, rt, svc);
        }
    }

    public static IReadOnlyList<(string Id, string Label, string? Error)> RestartOnline(
        ProjectCatalog catalog,
        ProjectRuntime rt,
        IReadOnlyDictionary<string, bool> health,
        IReadOnlySet<string>? ids = null)
    {
        var results = new List<(string Id, string Label, string? Error)>();
        foreach (var svc in OnlineRunnable(catalog, health, ids))
        {
            var err = RestartService(catalog, rt, svc);
            results.Add((svc.Id, svc.Label, err));
        }
        return results;
    }

    public static void StopService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        KillPidFile(rt, host.Stem);
        ReleasePort(host.Port);
        CloseLog(rt.LogPath(host.Stem));
    }

    public static string? RestartService(ProjectCatalog catalog, ProjectRuntime rt, ServiceEntry svc)
    {
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        StopService(catalog, rt, host);
        Thread.Sleep(800);
        return TryStartService(catalog, rt, host);
    }

    public static IReadOnlyList<string> StartAll(ProjectCatalog catalog, ProjectRuntime rt, int delayMs = 500)
    {
        var started = new List<string>();
        foreach (var svc in ServiceCatalogBuilder.OrderedRunnable(catalog))
        {
            if (ServiceCatalogBuilder.IsCurrentConsole(catalog, svc))
                continue;
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

    public static string DoctorReport(ProjectCatalog? catalog) => DoctorSnapshot.Build(catalog).ToText();

    internal static string? ResolvePreStartPath(string root, string? preStart)
    {
        if (string.IsNullOrWhiteSpace(preStart))
            return null;
        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, preStart.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = rootFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? full : null;
    }

    private static string? RunPreStart(ProjectCatalog catalog, ServiceEntry host, ServiceLogWriter writer)
    {
        if (string.IsNullOrWhiteSpace(host.PreStart))
            return null;
        var script = ResolvePreStartPath(catalog.Root, host.PreStart);
        if (script is null)
            return $"找不到 preStart（必須位於專案目錄內）：{host.PreStart}";

        writer.WriteLine($"=== preStart {host.PreStart} ===");
        var fileName = OperatingSystem.IsWindows() ? "powershell" : "pwsh";
        var args = new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script };
        var (code, output) = CliUtil.RunAsync(fileName, args, catalog.Root, timeoutMs: 300_000)
            .GetAwaiter()
            .GetResult();
        if (!string.IsNullOrWhiteSpace(output))
        {
            foreach (var line in output.Split('\n'))
                writer.WriteLine(line.TrimEnd('\r'));
        }
        if (code != 0)
            return $"{host.Label} 前置檢查失敗（exit {code}）。見 Log。";
        writer.WriteLine("=== preStart ok ===");
        return null;
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
