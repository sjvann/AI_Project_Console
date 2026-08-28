using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Tests;

public class ServiceActionPolicyTests
{
    [Fact]
    public void ForRow_Offline_ShowsStartNotRestartOrStop()
    {
        var flags = ServiceActionPolicy.ForRow(Svc("api", "http://localhost/"), online: false, self: false);

        Assert.False(flags.Open);
        Assert.True(flags.Start);
        Assert.False(flags.Restart);
        Assert.False(flags.Stop);
        Assert.False(flags.SelfHint);
        Assert.False(flags.HostedHint);
    }

    [Fact]
    public void ForRow_Online_ShowsOpenRestartStopNotStart()
    {
        var flags = ServiceActionPolicy.ForRow(Svc("api", "http://localhost/"), online: true, self: false);

        Assert.True(flags.Open);
        Assert.False(flags.Start);
        Assert.True(flags.Restart);
        Assert.True(flags.Stop);
    }

    [Fact]
    public void ForRow_Self_ShowsHintOnly()
    {
        var flags = ServiceActionPolicy.ForRow(Svc("console", "http://localhost:5080"), online: true, self: true);

        Assert.True(flags.Open);
        Assert.False(flags.Start);
        Assert.False(flags.Restart);
        Assert.False(flags.Stop);
        Assert.True(flags.SelfHint);
    }

    [Fact]
    public void ForRow_Hosted_ShowsHintNotLifecycle()
    {
        var flags = ServiceActionPolicy.ForRow(
            Svc("web", "http://localhost/", hostedBy: "api"),
            online: false,
            self: false);

        Assert.False(flags.Open);
        Assert.False(flags.Start);
        Assert.False(flags.Restart);
        Assert.False(flags.Stop);
        Assert.True(flags.HostedHint);
    }

    [Fact]
    public void ForGroup_AllOffline_ShowsStart()
    {
        var flags = ServiceActionPolicy.ForGroup(
        [
            (Svc("a"), false, false),
            (Svc("b"), false, false),
        ]);

        Assert.False(flags.Open);
        Assert.True(flags.Start);
        Assert.False(flags.Restart);
        Assert.False(flags.Stop);
    }

    [Fact]
    public void ForGroup_AllOnline_ShowsRestartStop()
    {
        var flags = ServiceActionPolicy.ForGroup(
        [
            (Svc("a"), true, false),
            (Svc("b"), true, false),
        ]);

        Assert.False(flags.Start);
        Assert.True(flags.Restart);
        Assert.True(flags.Stop);
    }

    [Fact]
    public void ForGroup_Open_OnlyWhenOnlineMemberHasUrl()
    {
        var flags = ServiceActionPolicy.ForGroup(
        [
            (Svc("a", "http://localhost/a"), true, false),
            (Svc("b"), false, false),
        ]);

        Assert.True(flags.Open);
        Assert.True(flags.Start);
        Assert.True(flags.Restart);
        Assert.True(flags.Stop);
    }

    [Fact]
    public void ForGroup_Mixed_ShowsStartAndStop()
    {
        var flags = ServiceActionPolicy.ForGroup(
        [
            (Svc("a", "http://localhost/a"), false, false),
            (Svc("b"), true, false),
        ]);

        Assert.False(flags.Open);
        Assert.True(flags.Start);
        Assert.True(flags.Restart);
        Assert.True(flags.Stop);
    }

    private static ServiceEntry Svc(string id, string openUrl = "", string? hostedBy = null) =>
        new(id, id, id, id, 80, "", openUrl, "Demo", hostedBy);
}
