using AiProject.Console.Core.ProcessOps;

namespace AiProject.Console.Core.Tests;

public class ServiceActivityMapTests
{
    [Theory]
    [InlineData(ServiceActivityMap.Starting, "啟動中…")]
    [InlineData(ServiceActivityMap.Stopping, "停止中…")]
    [InlineData(ServiceActivityMap.Restarting, "重啟中…")]
    [InlineData(ServiceActivityMap.Opening, "開啟中…")]
    [InlineData("unknown", null)]
    [InlineData(null, null)]
    public void Label_MatchesProjectStyleProgressText(string? activity, string? expected)
    {
        Assert.Equal(expected, ServiceActivityMap.Label(activity));
    }

    [Fact]
    public void Set_WritesActivityForEachId()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ServiceActivityMap.Set(map, ["api", "web"], ServiceActivityMap.Starting);

        Assert.Equal(ServiceActivityMap.Starting, map["api"]);
        Assert.Equal(ServiceActivityMap.Starting, map["web"]);
    }

    [Fact]
    public void IdsWithHosted_IncludesHostAndChildren()
    {
        var services = new List<ServiceEntry>
        {
            new("api", "API", "Api", "Api", 8080, "", "", "Lab"),
            new("ui", "UI", "Ui", "Ui", 8081, "", "", "Lab", "api"),
            new("other", "Other", "Other", "Other", 8090, "", "", "Other"),
        };

        var ids = ServiceActivityMap.IdsWithHosted(services, [services[0]]);

        Assert.Contains("api", ids);
        Assert.Contains("ui", ids);
        Assert.DoesNotContain("other", ids);
    }

    [Fact]
    public void IdsWithHosted_FromChild_StillMarksHostFamily()
    {
        var services = new List<ServiceEntry>
        {
            new("api", "API", "Api", "Api", 8080, "", "", "Lab"),
            new("ui", "UI", "Ui", "Ui", 8081, "", "", "Lab", "api"),
        };

        var ids = ServiceActivityMap.IdsWithHosted(services, [services[1]]);

        Assert.Contains("api", ids);
        Assert.Contains("ui", ids);
    }

    [Fact]
    public void Reconcile_KeepsStartingUntilHealthy()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Starting };
        var health = new Dictionary<string, bool> { ["api"] = false };

        var cleared = ServiceActivityMap.Reconcile(map, health);

        Assert.Equal(0, cleared);
        Assert.Equal(ServiceActivityMap.Starting, map["api"]);
    }

    [Fact]
    public void Reconcile_ClearsStartingWhenHealthy()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Starting };
        var health = new Dictionary<string, bool> { ["api"] = true };

        var cleared = ServiceActivityMap.Reconcile(map, health);

        Assert.Equal(1, cleared);
        Assert.Empty(map);
    }

    [Fact]
    public void Reconcile_ClearsStartingWhenFailed()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Starting };
        var health = new Dictionary<string, bool> { ["api"] = false };
        var errors = new Dictionary<string, string> { ["api"] = "行程立即結束，請查看 Log" };

        var cleared = ServiceActivityMap.Reconcile(map, health, errors);

        Assert.Equal(1, cleared);
        Assert.Empty(map);
    }

    [Fact]
    public void Reconcile_KeepsStoppingUntilOffline()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Stopping };
        var health = new Dictionary<string, bool> { ["api"] = true };

        var cleared = ServiceActivityMap.Reconcile(map, health);

        Assert.Equal(0, cleared);
        Assert.Equal(ServiceActivityMap.Stopping, map["api"]);
    }

    [Fact]
    public void Reconcile_ClearsStoppingWhenOffline()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Stopping };
        var health = new Dictionary<string, bool> { ["api"] = false };

        var cleared = ServiceActivityMap.Reconcile(map, health);

        Assert.Equal(1, cleared);
        Assert.Empty(map);
    }

    [Fact]
    public void Reconcile_KeepsRestartingUntilHealthyAgain()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Restarting };
        var health = new Dictionary<string, bool> { ["api"] = false };

        Assert.Equal(0, ServiceActivityMap.Reconcile(map, health));
        Assert.Equal(ServiceActivityMap.Restarting, map["api"]);

        health["api"] = true;
        Assert.Equal(1, ServiceActivityMap.Reconcile(map, health));
        Assert.Empty(map);
    }

    [Fact]
    public void ClearOpening_OnlyRemovesOpeningRows()
    {
        var map = new Dictionary<string, string>
        {
            ["api"] = ServiceActivityMap.Opening,
            ["web"] = ServiceActivityMap.Starting,
        };

        var cleared = ServiceActivityMap.ClearOpening(map);

        Assert.Equal(1, cleared);
        Assert.False(map.ContainsKey("api"));
        Assert.Equal(ServiceActivityMap.Starting, map["web"]);
    }

    [Fact]
    public void ClearFailed_DropsRowsWithErrors()
    {
        var map = new Dictionary<string, string>
        {
            ["api"] = ServiceActivityMap.Starting,
            ["web"] = ServiceActivityMap.Restarting,
        };
        var errors = new Dictionary<string, string> { ["api"] = "行程立即結束，請查看 Log" };

        var cleared = ServiceActivityMap.ClearFailed(map, errors);

        Assert.Equal(1, cleared);
        Assert.False(map.ContainsKey("api"));
        Assert.Equal(ServiceActivityMap.Restarting, map["web"]);
    }

    [Fact]
    public void Reconcile_OpeningAlwaysClears()
    {
        var map = new Dictionary<string, string> { ["api"] = ServiceActivityMap.Opening };

        var cleared = ServiceActivityMap.Reconcile(map, new Dictionary<string, bool>());

        Assert.Equal(1, cleared);
        Assert.Empty(map);
    }

    [Fact]
    public void Reconcile_ClearsStartingWhenPidDeadAndStillOffline()
    {
        var map = new Dictionary<string, string> { ["module"] = ServiceActivityMap.Starting };
        var health = new Dictionary<string, bool> { ["module"] = false };
        var dead = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "module" };

        var cleared = ServiceActivityMap.Reconcile(map, health, deadStarted: dead);

        Assert.Equal(1, cleared);
        Assert.Empty(map);
    }

    [Fact]
    public void Reconcile_KeepsStartingWhenPidAliveAndOffline()
    {
        var map = new Dictionary<string, string> { ["module"] = ServiceActivityMap.Starting };
        var health = new Dictionary<string, bool> { ["module"] = false };
        var dead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cleared = ServiceActivityMap.Reconcile(map, health, deadStarted: dead);

        Assert.Equal(0, cleared);
        Assert.Equal(ServiceActivityMap.Starting, map["module"]);
    }
}
