using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Runtime;

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
}
