using AiProject.Console.Core;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Tests;

public class DailyWorkflowTests
{
    [Fact]
    public void GitBriefStatus_Format_ShowsDirtyAndAheadBehind()
    {
        Assert.Equal("main · 乾淨", new GitBriefStatus("main", 0, 0, 0).Format());
        Assert.Equal("feat · 3 未提交 · ↑2 · ↓1", new GitBriefStatus("feat", 3, 2, 1).Format());
        Assert.Equal("dev · 乾淨", new GitBriefStatus("dev", 0, null, null).Format());
        Assert.Equal("main · ↑1", new GitBriefStatus("main", 0, 1, 0).Format());
    }

    [Fact]
    public void TextFilter_Apply_FiltersLinesCaseInsensitive()
    {
        var text = "INFO start\nERROR port 5101\ninfo ready";
        Assert.Equal(text, TextFilter.Apply(text, ""));
        Assert.Equal(text, TextFilter.Apply(text, "   "));
        Assert.Equal("ERROR port 5101", TextFilter.Apply(text, "error"));
        Assert.Equal("INFO start\ninfo ready", TextFilter.Apply(text, "info"));
        Assert.Equal("", TextFilter.Apply(text, "missing"));
    }
}
