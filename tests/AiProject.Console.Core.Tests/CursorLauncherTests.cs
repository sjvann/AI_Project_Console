using AiProject.Console.Core.Cursor;

namespace AiProject.Console.Core.Tests;

public class CursorLauncherTests
{
    [Fact]
    public void HasRuntimeErrors_IgnoresInfoAndWarn()
    {
        var log = """
            info: Microsoft.Hosting.Lifetime[14]
                  Now listening on: http://localhost:5101
            warn: Microsoft.AspNetCore.Server.Kestrel[0]
                  Overriding address(es) 'http://localhost:5101'.
            """;
        Assert.False(CursorLauncher.HasRuntimeErrors(log));
        Assert.Empty(CursorLauncher.ExtractRuntimeErrors(log));
    }

    [Fact]
    public void ExtractRuntimeErrors_CapturesFailAndContinuation()
    {
        var log = """
            info: Microsoft.Hosting.Lifetime[14]
                  Now listening on: http://localhost:5101
            fail: Microsoft.Extensions.Hosting.Internal.Host[11]
                  Hosting failed to start
                  System.IO.IOException: Failed to bind to address
            """;
        Assert.True(CursorLauncher.HasRuntimeErrors(log));
        var errors = CursorLauncher.ExtractRuntimeErrors(log);
        Assert.Single(errors);
        Assert.Contains("Hosting failed to start", errors[0]);
        Assert.Contains("Failed to bind", errors[0]);
    }

    [Fact]
    public void HasRuntimeErrors_DetectsUnhandledException()
    {
        Assert.True(CursorLauncher.HasRuntimeErrors("Unhandled exception. System.InvalidOperationException: boom"));
    }

    [Fact]
    public void BuildRuntimeLogPrompt_IncludesServiceAndLog()
    {
        var prompt = CursorLauncher.BuildRuntimeLogPrompt(
            @"C:\proj",
            "Hub Api",
            "fail: Hub.Api[0]\n      boom",
            "啟動逾時");
        Assert.Contains("服務執行發生錯誤", prompt);
        Assert.Contains("Hub Api", prompt);
        Assert.Contains("啟動逾時", prompt);
        Assert.Contains("boom", prompt);
    }
}
