using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Tests;

public class ServiceOpenEnsureTests
{
    [Fact]
    public void CanOpenUrl_NoResult_AllowsOpen()
    {
        Assert.True(ServiceOpenEnsure.CanOpenUrl("web", []));
    }

    [Fact]
    public void CanOpenUrl_SelfOk_AllowsOpen()
    {
        Assert.True(ServiceOpenEnsure.CanOpenUrl("web",
        [
            ("fhir", "Profile", null),
            ("web", "App", null),
        ]));
    }

    [Fact]
    public void CanOpenUrl_SelfFailed_BlocksOpen()
    {
        Assert.False(ServiceOpenEnsure.CanOpenUrl("web",
        [
            ("fhir", "Profile", "未就緒"),
            ("web", "App", "相依「Profile」未就緒"),
        ]));
    }

    [Fact]
    public void CanOpenUrl_OtherFailed_StillAllowsTarget()
    {
        Assert.True(ServiceOpenEnsure.CanOpenUrl("web",
        [
            ("opt", "Optional", "略過失敗"),
            ("web", "App", null),
        ]));
    }

    [Fact]
    public void WithOpenUrl_FiltersEmpty()
    {
        var list = ServiceOpenEnsure.WithOpenUrl(
        [
            new("a", "a", "a", "a", 1, "", "http://localhost/a", "G"),
            new("b", "b", "b", "b", 2, "", "", "G"),
        ]);
        Assert.Equal(["a"], list.Select(s => s.Id));
    }
}
