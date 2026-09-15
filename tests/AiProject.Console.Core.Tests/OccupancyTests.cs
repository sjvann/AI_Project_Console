using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class OccupancyTests
{
    [Fact]
    public void Infer_BuildAndMcpTitles()
    {
        Assert.Equal(OccupancyKind.Build, OccupancyKindLookup.Infer("建置中…"));
        Assert.Equal(OccupancyKind.Build, OccupancyKindLookup.Infer("編譯 Foo…"));
        Assert.Equal(OccupancyKind.Test, OccupancyKindLookup.Infer("測試中…"));
        Assert.Equal(OccupancyKind.McpBuild, OccupancyKindLookup.Infer("Agent 正在編譯"));
        Assert.Equal(OccupancyKind.Git, OccupancyKindLookup.Infer("正在從遠端同步…"));
    }

    [Fact]
    public void BarTitle_IncludesProgressAndCurrent()
    {
        var snap = OccupancySnapshot.Begin(OccupancyKind.Build, "建置中…", OccupancySources.Desktop, 1, total: 12, currentName: "Foo");
        snap = snap.WithProgress(3, 12, "Foo");
        Assert.Equal("建置中 3/12 · Foo", snap.BarTitle);
        Assert.StartsWith("忙碌中：建置中 3/12 · Foo", snap.BusyReason);
        Assert.Contains("— Demo", snap.WindowCaption("Demo"));
    }

    [Fact]
    public void McpSource_UsesAgentHeadline()
    {
        var snap = OccupancySnapshot.Begin(OccupancyKind.McpBuild, "建置中…", OccupancySources.Mcp, 9, total: 2);
        Assert.Equal("Agent 正在編譯", snap.Headline);
        Assert.False(snap.CanCancel);
    }

    [Fact]
    public void Idle_IsInactive()
    {
        Assert.False(OccupancySnapshot.Idle.IsActive);
        Assert.Equal("", OccupancySnapshot.Idle.BusyReason);
    }
}
