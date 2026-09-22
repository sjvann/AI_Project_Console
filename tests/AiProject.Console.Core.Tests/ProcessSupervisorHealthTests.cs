using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tests;

public class ProcessSupervisorHealthTests
{
    [Fact]
    public async Task ProbeHealth_MutexScheme_TracksNamedMutex()
    {
        var name = @"Local\AiProject.Test." + Guid.NewGuid().ToString("N");
        var svc = new ServiceEntry("x", "X", "X", "X", null, "mutex:" + name, "", "g");
        Assert.False(await ProcessSupervisor.ProbeHealthAsync(svc));
        using var mutex = new Mutex(true, name, out var created);
        Assert.True(created);
        Assert.True(await ProcessSupervisor.ProbeHealthAsync(svc));
    }

    [Fact]
    public async Task ProbeHealth_TcpScheme_TracksListeningPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var svc = new ServiceEntry("x", "X", "X", "X", null, "tcp:" + port, "", "g");
            Assert.True(await ProcessSupervisor.ProbeHealthAsync(svc));
        }
        finally
        {
            listener.Stop();
        }
        var offline = new ServiceEntry("x", "X", "X", "X", null, "tcp:" + port, "", "g");
        Assert.False(await ProcessSupervisor.ProbeHealthAsync(offline));
    }

    [Fact]
    public async Task ProbeAllHealth_RunsInParallelAndKeepsIds()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var root = Path.Combine(Path.GetTempPath(), "probe-all-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "t",
                Services =
                [
                    new ServiceEntry("on", "On", "On", "On", null, "tcp:" + port, "", "g"),
                    new ServiceEntry("off", "Off", "Off", "Off", null, "tcp:" + (port == 1 ? 2 : 1), "", "g"),
                ],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = new JsonObject(),
                Scan = new ScanResult(root, []),
            };

            var health = await ProcessSupervisor.ProbeAllHealthAsync(catalog);

            Assert.True(health["on"]);
            Assert.False(health["off"]);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeadStartedIds_MarksStalePidAndHostedChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-pid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rt = new ProjectRuntime(root);
            rt.Ensure();
            File.WriteAllText(rt.PidPath("Host"), "999999999\n");
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "t",
                Services =
                [
                    new ServiceEntry("host", "Host", "Host", "Host", null, "", "", "g"),
                    new ServiceEntry("child", "Child", "Child", "Child", null, "", "", "g", "host"),
                    new ServiceEntry("other", "Other", "Other", "Other", null, "", "", "g"),
                ],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = new JsonObject(),
                Scan = new ScanResult(root, []),
            };

            var dead = ProcessSupervisor.DeadStartedIds(catalog, rt);

            Assert.Contains("host", dead);
            Assert.Contains("child", dead);
            Assert.DoesNotContain("other", dead);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolvePreStartCommand_Python_UsesLauncher()
    {
        var script = Path.Combine(Path.GetTempPath(), "ensure-" + Guid.NewGuid().ToString("N") + ".py");
        File.WriteAllText(script, "print('ok')\n");
        try
        {
            if (!ProcessSupervisor.HasPythonLauncher())
            {
                Assert.False(ProcessSupervisor.TryResolvePreStartCommand(script, out _, out _, out var missing));
                Assert.Contains("Python", missing);
                return;
            }

            Assert.True(ProcessSupervisor.TryResolvePreStartCommand(script, out var fileName, out var args, out var error));
            Assert.Null(error);
            Assert.False(string.IsNullOrWhiteSpace(fileName));
            Assert.DoesNotContain("powershell", fileName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pwsh", fileName, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-File", args);
            Assert.Contains(script, args);
            if (ProcessSupervisor.IsPyLauncher(fileName))
                Assert.Contains("-3", args);
        }
        finally
        {
            File.Delete(script);
        }
    }

    [Fact]
    public void RequiresPython_CsHostWithPyPreStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "req-py-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Host"));
        File.WriteAllText(Path.Combine(root, "src", "Host", "Host.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");
        File.WriteAllText(Path.Combine(root, "ai-project.json"), """
            {
              "name": "PyPre",
              "services": [
                {
                  "id": "host",
                  "label": "Host",
                  "project": "src/Host",
                  "port": 5080,
                  "preStart": "Scripts/ensure-profile.py"
                }
              ]
            }
            """);
        try
        {
            var catalog = ServiceCatalogBuilder.Build(root);
            var svc = catalog.Services[0];
            Assert.True(ProcessSupervisor.RequiresDotnet(catalog, svc));
            Assert.True(ProcessSupervisor.RequiresPython(catalog, svc));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryResolvePreStartCommand_Ps1_UsesPowerShell()
    {
        Assert.True(ProcessSupervisor.TryResolvePreStartCommand("scripts/ensure.ps1", out var fileName, out var args, out var error));
        Assert.Null(error);
        Assert.Equal(OperatingSystem.IsWindows() ? "powershell" : "pwsh", fileName);
        Assert.Contains("-File", args);
        Assert.Contains("scripts/ensure.ps1", args);
    }

    [Fact]
    public void TryResolvePreStartCommand_UnknownExtension_Fails()
    {
        Assert.False(ProcessSupervisor.TryResolvePreStartCommand("tools/ensure.bat", out _, out _, out var error));
        Assert.Contains(".py", error);
    }

    [Fact]
    public async Task ResolvePreStartCommand_PythonScript_ReportsExitCode()
    {
        if (!ProcessSupervisor.HasPythonLauncher())
            return;

        var root = Path.Combine(Path.GetTempPath(), "prestart-py-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ok = Path.Combine(root, "ok.py");
            File.WriteAllText(ok, "print('prestart-ok')\n");
            Assert.True(ProcessSupervisor.TryResolvePreStartCommand(ok, out var fileName, out var args, out _));
            var (code, output) = await CliUtil.RunAsync(fileName, args, root);
            Assert.Equal(0, code);
            Assert.Contains("prestart-ok", output);

            var fail = Path.Combine(root, "fail.py");
            File.WriteAllText(fail, "raise SystemExit(7)\n");
            Assert.True(ProcessSupervisor.TryResolvePreStartCommand(fail, out fileName, out args, out _));
            (code, _) = await CliUtil.RunAsync(fileName, args, root);
            Assert.Equal(7, code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
