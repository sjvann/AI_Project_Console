using System.Text.Json.Nodes;
using AiProject.Console.Core;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Infra;
using AiProject.Console.Core.ProcessOps;

namespace AiProject.Console.Core.Tests;

public class WorkspaceRuntimeTests
{
    [Fact]
    public void Read_ParsesRuntimesAndDatastores()
    {
        var manifest = JsonNode.Parse("""
        {
          "runtimes": [
            { "id": "podman", "kind": "podman-machine" }
          ],
          "data_stores": [
            {
              "id": "company-db",
              "label": "公司庫",
              "runtime": "podman",
              "container": "company-db",
              "port": 5432,
              "required_by": ["test", "company-web"]
            }
          ]
        }
        """)!.AsObject();

        var (runtimes, stores) = WorkspaceRuntimeManifest.Read(manifest);
        Assert.Equal("podman", runtimes[0].Id);
        Assert.Equal(WorkspaceRuntimeManifest.DefaultMachineName, runtimes[0].Machine);
        Assert.Equal("公司庫", stores[0].Label);
        Assert.Equal("company-db", stores[0].Container);
        Assert.Equal(5432, stores[0].Port);
        Assert.Equal(["test", "company-web"], stores[0].RequiredBy);
    }

    [Fact]
    public void Build_LoadsDeclarationsOntoCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "ai-project.json"), """
            {
              "name": "Demo",
              "scanProjects": false,
              "services": [
                { "id": "api", "project": "src/Demo.Api", "port": 8080 }
              ],
              "runtimes": [
                { "id": "podman", "kind": "podman-machine", "machine": "dev-vm" }
              ],
              "datastores": [
                { "id": "db", "runtime": "podman", "port": 5432, "requiredBy": "test" }
              ]
            }
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("dev-vm", catalog.Runtimes[0].Machine);
            Assert.Equal(["test"], catalog.Datastores[0].RequiredBy);
            Assert.Equal(5432, catalog.Datastores[0].Port);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void StoppedMachine_BlocksTestAndNamedService_NotUnrelated()
    {
        var report = Evaluate(Stopped("podman-machine-default"));
        Assert.Equal("Podman 虛擬機未啟動：podman-machine-default", report.Attention);
        Assert.Equal(1, report.BlockedCount);
        Assert.Equal("錯誤 1 筆：Podman 虛擬機未啟動：podman-machine-default", report.CheckTip);
        Assert.DoesNotContain("資料庫未就緒", report.Attention);
        Assert.Equal("Podman 虛擬機未啟動：podman-machine-default", RuntimeGate.TestMessage(report));
        Assert.Equal("Podman 虛擬機未啟動：podman-machine-default", RuntimeGate.ServiceMessage(report, ["company-web"]));
        Assert.Null(RuntimeGate.ServiceMessage(report, ["other"]));
        Assert.Equal("podman machine start podman-machine-default", report.Runtimes[0].HowTo);
        Assert.Equal("podman start company-db", report.Datastores[0].HowTo);
    }

    [Fact]
    public void DatastoreStart_OverridesContainerCommand()
    {
        var catalog = Sample(stores:
        [
            new DatastoreDeclaration(
                "company-db", "公司庫", "podman", "company-db", 5432, ["test"],
                "py -3 deploy/scripts/start_postgres_podman.py"),
        ]);
        var report = WorkspaceRuntimeEvaluator.Evaluate(catalog, Stopped("podman-machine-default"));
        Assert.Equal("podman machine start podman-machine-default", report.Runtimes[0].HowTo);
        Assert.Equal("py -3 deploy/scripts/start_postgres_podman.py", report.Datastores[0].HowTo);
    }

    [Fact]
    public void MissingCli_NamesTheMachine()
    {
        var report = Evaluate(PodmanSnapshot.NoCli);
        Assert.Equal("找不到 podman 命令，無法確認虛擬機 podman-machine-default。", RuntimeGate.TestMessage(report));
    }

    [Fact]
    public void MachineMissing_SaysItDoesNotExist()
    {
        var report = Evaluate(Listed());
        Assert.Equal("Podman 虛擬機不存在：podman-machine-default", report.Attention);
    }

    [Fact]
    public void Starting_IsNotTreatedAsStopped()
    {
        var report = Evaluate(Listed(("podman-machine-default", PodmanMachineObservation.Starting)));
        Assert.Equal("Podman 虛擬機啟動中：podman-machine-default", report.Attention);
    }

    [Fact]
    public void RunningWithoutContainer_SaysDatabaseNotReady()
    {
        var snapshot = Listed(("podman-machine-default", PodmanMachineObservation.Running));
        snapshot = snapshot with { ContainersKnown = true, RunningContainers = new HashSet<string>(StringComparer.Ordinal) };
        var report = Evaluate(snapshot, portOpen: _ => true);
        Assert.Equal("虛擬機已啟動，資料庫未就緒：公司庫", RuntimeGate.TestMessage(report));
        Assert.Contains("容器 company-db 不在執行中", report.Datastores[0].Detail);
    }

    [Fact]
    public void RunningWithClosedPort_SaysDatabaseNotReady()
    {
        var snapshot = Listed(("podman-machine-default", PodmanMachineObservation.Running)) with
        {
            ContainersKnown = true,
            RunningContainers = new HashSet<string>(StringComparer.Ordinal) { "company-db" },
        };
        var report = Evaluate(snapshot, portOpen: _ => false);
        Assert.Equal("虛擬機已啟動，資料庫未就緒：公司庫", report.Attention);
        Assert.Contains("5432", report.Datastores[0].Detail);
    }

    [Fact]
    public void Ready_DoesNotBlockTestOrStart()
    {
        var snapshot = Listed(("podman-machine-default", PodmanMachineObservation.Running)) with
        {
            ContainersKnown = true,
            RunningContainers = new HashSet<string>(StringComparer.Ordinal) { "company-db" },
        };
        var report = Evaluate(snapshot, portOpen: port => port == 5432);
        Assert.Null(report.Attention);
        Assert.Contains("Podman 虛擬機已啟動：podman-machine-default", report.CheckTip);
        Assert.Null(RuntimeGate.TestMessage(report));
        Assert.Null(RuntimeGate.ServiceMessage(report, ["company-web"]));
    }

    [Fact]
    public void ServiceOnlyRequirement_DoesNotBlockTests()
    {
        var catalog = Sample(requiredBy: ["company-web"]);
        var report = WorkspaceRuntimeEvaluator.Evaluate(catalog, PodmanSnapshot.NoCli);
        Assert.Null(RuntimeGate.TestMessage(report));
        Assert.NotNull(RuntimeGate.ServiceMessage(report, ["company-web"]));
    }

    [Fact]
    public void Undeclared_DoesNotBlock()
    {
        var catalog = Sample(runtimes: [], stores: []);
        var report = WorkspaceRuntimeEvaluator.Evaluate(catalog, PodmanSnapshot.NoCli);
        Assert.Null(report.Attention);
        Assert.Null(RuntimeGate.TestMessage(report));
    }

    [Fact]
    public void Doctor_IncludesBlockedHeadline()
    {
        var report = Evaluate(Stopped("podman-machine-default"));
        var doctor = DoctorSnapshot.Build(Sample(), report);
        var section = doctor.Sections.Single(s => s.Id == "runtime");
        Assert.Contains(section.Items, item => item.Value.Contains("Podman 虛擬機未啟動：podman-machine-default", StringComparison.Ordinal));
        Assert.Contains("Podman 虛擬機未啟動：podman-machine-default", doctor.ToText());
    }

    [Fact]
    public void ParseList_ReadsRunningFlag_AndSkipsPreamble()
    {
        var map = PodmanMachineListParser.Parse("""
        time="2026-09-28" level=warning msg="hint"
        [{"Name":"podman-machine-default","Running":false,"Starting":true}]
        """);
        Assert.Equal(PodmanMachineObservation.Starting, map["podman-machine-default"]);
    }

    [Fact]
    public void ParseContainers_SplitsLinesAndCommas()
    {
        var names = PodmanContainerNames.Parse("company-db\nother,extra\n");
        Assert.Contains("company-db", names);
        Assert.Contains("extra", names);
    }

    static WorkspaceRuntimeReport Evaluate(PodmanSnapshot snapshot, Func<int, bool>? portOpen = null) =>
        WorkspaceRuntimeEvaluator.Evaluate(Sample(), snapshot, portOpen);

    static PodmanSnapshot Stopped(string name) =>
        Listed((name, PodmanMachineObservation.Stopped));

    static PodmanSnapshot Listed(params (string Name, PodmanMachineObservation State)[] machines)
    {
        var map = new Dictionary<string, PodmanMachineObservation>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, state) in machines)
            map[name] = state;
        return new PodmanSnapshot(true, true, map, false, new HashSet<string>(StringComparer.Ordinal), null);
    }

    static ProjectCatalog Sample(IReadOnlyList<string>? requiredBy = null, IReadOnlyList<RuntimeDeclaration>? runtimes = null, IReadOnlyList<DatastoreDeclaration>? stores = null)
    {
        runtimes ??= [new RuntimeDeclaration("podman", WorkspaceRuntimeManifest.PodmanMachineKind, "podman-machine-default")];
        stores ??=
        [
            new DatastoreDeclaration(
                "company-db",
                "公司庫",
                "podman",
                "company-db",
                5432,
                requiredBy ?? ["test", "company-web"]),
        ];
        return new ProjectCatalog
        {
            Root = "workspace",
            Name = "Demo",
            Services = [],
            Projects = [],
            StartOrder = [],
            Frontend = "",
            Manifest = new JsonObject(),
            Scan = new ScanResult("workspace", []),
            Runtimes = runtimes,
            Datastores = stores,
        };
    }
}
