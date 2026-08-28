using System.Text.Json.Nodes;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Scan;

namespace AiProject.Console.Core.Tests;

public class ServiceGroupStartTests
{
    [Fact]
    public void OfflineRunnable_GroupFilter_KeepsStartOrder()
    {
        var catalog = Catalog();
        var health = new Dictionary<string, bool>();
        var lab = ServiceGroupTree.Build(catalog.Services)[0];
        var ids = lab.Descendants().Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var planned = ProcessSupervisor.OfflineRunnable(catalog, health, ids);

        Assert.Equal(["sftp-api", "hl7-api"], planned.Select(s => s.Id));
    }

    [Fact]
    public void OfflineRunnable_SkipsOnlineAndOutOfGroup()
    {
        var catalog = Catalog();
        var health = new Dictionary<string, bool> { ["hl7-api"] = true };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hl7-api", "sftp-api" };

        var planned = ProcessSupervisor.OfflineRunnable(catalog, health, ids);

        Assert.Equal(["sftp-api"], planned.Select(s => s.Id));
    }

    [Fact]
    public void OnlineRunnable_OnlyOnlineInGroup()
    {
        var catalog = Catalog();
        var health = new Dictionary<string, bool>
        {
            ["hl7-api"] = true,
            ["sftp-api"] = false,
            ["weave"] = true,
        };
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hl7-api", "sftp-api" };

        var online = ProcessSupervisor.OnlineRunnable(catalog, health, ids);

        Assert.Equal(["hl7-api"], online.Select(s => s.Id));
    }

    private static ProjectCatalog Catalog() => new()
    {
        Root = "/",
        Name = "AION",
        Services =
        [
            new ServiceEntry("hl7-api", "HL7 API", "Hl7.Api", "Lab/HL7", 5110, "", "", "Lab/HL7"),
            new ServiceEntry("sftp-api", "SFTP API", "Sftp.Api", "Lab/SFTP", 5120, "", "", "Lab/SFTP"),
            new ServiceEntry("weave", "Weave", "Weave", "Weave", 8088, "", "", "Weave"),
        ],
        Projects = [],
        StartOrder = ["sftp-api", "hl7-api", "weave"],
        Frontend = "",
        Manifest = new JsonObject(),
        Scan = new ScanResult("/", []),
    };
}
