using AiProject.Console.Core.ProcessOps;

namespace AiProject.Console.Core.Tests;

public class StartErrorMapTests
{
    [Fact]
    public void Apply_Success_RemovesPreviousFailure()
    {
        var errors = new Dictionary<string, string> { ["profile"] = "行程立即結束，請查看 Log" };

        StartErrorMap.Apply(errors, "profile", null);

        Assert.Empty(errors);
    }

    [Fact]
    public void Apply_Failure_RecordsMessage()
    {
        var errors = new Dictionary<string, string>();

        StartErrorMap.Apply(errors, "profile", "找不到專案");

        Assert.Equal("找不到專案", errors["profile"]);
    }

    [Fact]
    public void ClearHealthy_DropsRecoveredServicesOnly()
    {
        var errors = new Dictionary<string, string>
        {
            ["profile"] = "行程立即結束，請查看 Log",
            ["weave"] = "找不到專案",
        };
        var health = new Dictionary<string, bool>
        {
            ["profile"] = true,
            ["weave"] = false,
        };

        var cleared = StartErrorMap.ClearHealthy(errors, health);

        Assert.Equal(1, cleared);
        Assert.False(errors.ContainsKey("profile"));
        Assert.Equal("找不到專案", errors["weave"]);
    }
}
