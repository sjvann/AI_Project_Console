using System.Text.Json.Nodes;
using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core;

namespace AiProject.Console.Core.Tests;

public class ServiceStartPlannerTests
{
    [Fact]
    public void HardDep_ComesFirst()
    {
        var catalog = Cat(
            Svc("api"),
            Svc("web", deps: [new ServiceDependency("api")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["api", "web"], plan.Order.Select(s => s.Id).ToArray());
        Assert.DoesNotContain("api", plan.OptionalIds);
    }

    [Fact]
    public void OptionalDep_IncludedByDefault_AndMarkedOptional()
    {
        var catalog = Cat(
            Svc("fhir-host"),
            Svc("chronicle-web", deps: [new ServiceDependency("fhir-host", Optional: true)]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["chronicle-web"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["fhir-host", "chronicle-web"], plan.Order.Select(s => s.Id).ToArray());
        Assert.Contains("fhir-host", plan.OptionalIds);
        Assert.DoesNotContain("chronicle-web", plan.OptionalIds);
    }

    [Fact]
    public void SkipOptional_OmitsSoftDeps()
    {
        var catalog = Cat(
            Svc("fhir-host"),
            Svc("api"),
            Svc("web", deps:
            [
                new ServiceDependency("fhir-host", Optional: true),
                new ServiceDependency("api"),
            ]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"], skipOptional: true);
        Assert.Empty(plan.Errors);
        Assert.Equal(["api", "web"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void SkipDepends_OnlyTarget()
    {
        var catalog = Cat(
            Svc("api"),
            Svc("web", deps: [new ServiceDependency("api")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"], skipDepends: true);
        Assert.Empty(plan.Errors);
        Assert.Equal(["web"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Cycle_IsError()
    {
        var catalog = Cat(
            Svc("a", deps: [new ServiceDependency("b")]),
            Svc("b", deps: [new ServiceDependency("a")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["a"]);
        Assert.Contains(plan.Errors, e => e.Contains("循環"));
    }

    [Fact]
    public void UnknownHardDep_IsError()
    {
        var catalog = Cat(Svc("web", deps: [new ServiceDependency("missing-api")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"]);
        Assert.Contains(plan.Errors, e => e.Contains("missing-api"));
    }

    [Fact]
    public void UnknownOptionalDep_IsIgnored()
    {
        var catalog = Cat(Svc("web", deps: [new ServiceDependency("public-fhir", Optional: true)]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["web"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void HostedBy_ResolvesToHost()
    {
        var catalog = Cat(
            Svc("api"),
            Svc("web-alias", hostedBy: "api"),
            Svc("client", deps: [new ServiceDependency("web-alias")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["client"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["api", "client"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Transitive_HardDeps()
    {
        var catalog = Cat(
            Svc("db"),
            Svc("api", deps: [new ServiceDependency("db")]),
            Svc("web", deps: [new ServiceDependency("api")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["db", "api", "web"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void TargetThatIsAlsoOptionalDep_IsHard()
    {
        var catalog = Cat(
            Svc("fhir-host"),
            Svc("chronicle-web", deps: [new ServiceDependency("fhir-host", Optional: true)]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["chronicle-web", "fhir-host"]);
        Assert.Empty(plan.Errors);
        Assert.DoesNotContain("fhir-host", plan.OptionalIds);
        Assert.Contains("fhir-host", plan.TargetIds);
    }

    [Fact]
    public void MixedSoftAndHard_AcrossTargets()
    {
        var catalog = Cat(
            Svc("fhir-host"),
            Svc("lab-api"),
            Svc("chronicle-web", deps: [new ServiceDependency("fhir-host", Optional: true)]),
            Svc("lab-web", deps: [new ServiceDependency("lab-api")]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["chronicle-web", "lab-web"]);
        Assert.Empty(plan.Errors);
        Assert.Contains("fhir-host", plan.OptionalIds);
        Assert.DoesNotContain("lab-api", plan.OptionalIds);
        Assert.Equal(4, plan.Order.Count);
        Assert.True(IndexOf(plan, "lab-api") < IndexOf(plan, "lab-web"));
        Assert.True(IndexOf(plan, "fhir-host") < IndexOf(plan, "chronicle-web"));
    }

    [Fact]
    public void HostedTarget_IsError()
    {
        var catalog = Cat(
            Svc("api"),
            Svc("web", hostedBy: "api"));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["web"]);
        Assert.Contains(plan.Errors, e => e.Contains("隨"));
        Assert.Empty(plan.Order);
    }

    [Fact]
    public void GroupTargets_ExpandDepsOutsideGroup()
    {
        var catalog = Cat(
            Svc("fhir-host", group: "Ledger"),
            Svc("chronicle-web", group: "Solution", deps: [new ServiceDependency("fhir-host", Optional: true)]));
        var plan = ServiceStartPlanner.ForTargets(catalog, ["chronicle-web"]);
        Assert.Empty(plan.Errors);
        Assert.Equal(["fhir-host", "chronicle-web"], plan.Order.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task WaitUntilReady_EmptyProbe_ReturnsFalse()
    {
        var svc = Svc("x");
        svc = svc with { Port = null, Health = "", Ready = null };
        var catalog = Cat(svc);
        Assert.False(await ProcessSupervisor.WaitUntilReadyAsync(catalog, svc, timeoutMs: 1));
    }

    static int IndexOf(ServiceStartPlan plan, string id) =>
        plan.Order.Select((s, i) => (s.Id, i)).First(x => x.Id == id).i;

    static ServiceEntry Svc(
        string id,
        string? hostedBy = null,
        IReadOnlyList<ServiceDependency>? deps = null,
        string group = "G") =>
        new(id, id, id, "src/" + id, 1, "", "", group, HostedBy: hostedBy, DependsOn: deps);

    static ProjectCatalog Cat(params ServiceEntry[] services) => new()
    {
        Root = Path.GetTempPath(),
        Name = "t",
        Services = services,
        Projects = [],
        StartOrder = services.Where(s => s.HostedBy is null).Select(s => s.Id).ToList(),
        Frontend = "",
        Manifest = new JsonObject(),
        Scan = new ScanResult(Path.GetTempPath(), []),
    };
}
