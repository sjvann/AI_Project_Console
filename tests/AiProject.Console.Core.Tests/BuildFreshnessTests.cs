using AiProject.Console.Core.Build;
using AiProject.Console.Core.Runtime;

namespace AiProject.Console.Core.Tests;

public class BuildFreshnessTests
{
    [Theory]
    [InlineData("Properties/launchSettings.json", true)]
    [InlineData("launchSettings.json", true)]
    [InlineData("appsettings.json", true)]
    [InlineData("appsettings.Development.json", true)]
    [InlineData("Program.cs", false)]
    [InlineData("wwwroot/data.json", false)]
    public void IsRuntimeConfig_RecognizesLaunchAndAppSettings(string relative, bool expected)
    {
        Assert.Equal(expected, BuildFreshness.IsRuntimeConfig(relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Theory]
    [InlineData("data/twins.json", true)]
    [InlineData("data/senml.json", true)]
    [InlineData("logs/app.log", true)]
    [InlineData("App_Data/state.json", true)]
    [InlineData("wwwroot/data.json", false)]
    [InlineData("Program.cs", false)]
    [InlineData("config/hub.json", false)]
    public void IsRuntimeState_SkipsLiveDataJson(string relative, bool expected)
    {
        Assert.Equal(expected, BuildFreshness.IsRuntimeState(relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void ProjectStaysFresh_WhenOnlyLaunchSettingsIsNewerThanDll()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var launch = Path.Combine(projectDir, "Properties", "launchSettings.json");
            var now = DateTime.UtcNow;
            StampSources(projectDir, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-5));
            File.SetLastWriteTimeUtc(launch, now);
            File.SetLastWriteTimeUtc(Path.Combine(projectDir, "appsettings.json"), now);

            var (srcMtime, srcPath) = BuildFreshness.NewestSource(projectDir);
            var (outMtime, _) = BuildFreshness.BuildOutput(projectDir);
            Assert.False(srcPath!.EndsWith("launchSettings.json", StringComparison.OrdinalIgnoreCase));
            Assert.True(srcMtime <= outMtime);
            Assert.Equal("fresh", BuildFreshness.ProjectBuildState(root, DemoApiInfo()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectStaysFresh_WhenOnlyRuntimeDataJsonIsNewerThanDll()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var dataDir = Path.Combine(projectDir, "data");
            Directory.CreateDirectory(dataDir);
            var twins = Path.Combine(dataDir, "twins.json");
            File.WriteAllText(twins, "{ }");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            StampSources(projectDir, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-5));
            File.SetLastWriteTimeUtc(twins, now);

            var (srcMtime, srcPath) = BuildFreshness.NewestSource(projectDir);
            var (outMtime, _) = BuildFreshness.BuildOutput(projectDir);
            Assert.False(srcPath!.Replace('\\', '/').Contains("/data/", StringComparison.OrdinalIgnoreCase));
            Assert.True(srcMtime <= outMtime);
            Assert.Equal("fresh", BuildFreshness.ProjectBuildState(root, DemoApiInfo()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectIsStale_WhenCsIsNewerThanDll()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var cs = Path.Combine(projectDir, "Program.cs");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-5));
            File.SetLastWriteTimeUtc(cs, now);

            var state = BuildFreshness.ProjectBuildState(root, DemoApiInfo());
            Assert.Equal("stale", state.Status);
            Assert.Equal("C#", state.Language);
            Assert.Contains("Program.cs", state.Reason);
            Assert.NotNull(state.LastBuildUtc);
            Assert.Equal("需重編", BuildFreshness.BadgeText(state));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSuccessReport_OverridesStaleDllMtime()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var cs = Path.Combine(projectDir, "Program.cs");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-20));
            File.SetLastWriteTimeUtc(cs, now.AddMinutes(-2));
            Assert.Equal("stale", BuildFreshness.ProjectBuildState(root, DemoApiInfo()).Status);

            var runtime = new ProjectRuntime(root);
            BuildReportStore.Write(runtime, projectDir, 0, BuildFreshness.DefaultConfiguration);

            var state = BuildFreshness.ProjectBuildState(root, DemoApiInfo());
            Assert.Equal("fresh", state.Status);
            Assert.Contains("控制台編譯成功", state.Reason);
            Assert.Equal("最新", BuildFreshness.BadgeText(state));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SourceNewerThanConsoleReport_StaysStale()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var cs = Path.Combine(projectDir, "Program.cs");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-20));
            File.SetLastWriteTimeUtc(cs, now.AddMinutes(-10));
            var runtime = new ProjectRuntime(root);
            var path = BuildReportStore.Write(runtime, projectDir, 0, BuildFreshness.DefaultConfiguration);
            File.WriteAllText(path, """
                {
                  "target": "Demo.Api",
                  "exitCode": 0,
                  "configuration": "Debug",
                  "status": "ok",
                  "completedUtc": "2020-01-01T00:00:00Z"
                }
                """);
            File.SetLastWriteTimeUtc(cs, DateTime.UtcNow);

            var state = BuildFreshness.ProjectBuildState(root, DemoApiInfo());
            Assert.Equal("stale", state.Status);
            Assert.Contains("Program.cs", state.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FailedConsoleReport_KeepsStaleAndMentionsFailure()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var cs = Path.Combine(projectDir, "Program.cs");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(cs, now);
            var runtime = new ProjectRuntime(root);
            BuildReportStore.Write(runtime, projectDir, 1, BuildFreshness.DefaultConfiguration);

            var state = BuildFreshness.ProjectBuildState(root, DemoApiInfo());
            Assert.Equal("stale", state.Status);
            Assert.Contains("控制台上次編譯失敗", state.Reason);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectStaysFresh_WhenOnlyConfigJsonIsNewerThanDll()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var cfgDir = Path.Combine(projectDir, "config");
            Directory.CreateDirectory(cfgDir);
            var hub = Path.Combine(cfgDir, "hub.json");
            File.WriteAllText(hub, "{ }");
            var dll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var now = DateTime.UtcNow;
            StampSources(projectDir, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(dll, now.AddMinutes(-5));
            File.SetLastWriteTimeUtc(hub, now);

            var state = BuildFreshness.ProjectBuildState(root, DemoApiInfo());
            Assert.Equal("fresh", state.Status);
            Assert.DoesNotContain("hub.json", state.NewestSourcePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PrefersDebugDll_OverNewerRelease()
    {
        var root = CreateProject("Demo.Api");
        try
        {
            var projectDir = Path.Combine(root, "src", "Demo.Api");
            var rel = Path.Combine(projectDir, "bin", "Release", "net8.0");
            Directory.CreateDirectory(rel);
            var debugDll = Path.Combine(projectDir, "bin", "Debug", "net8.0", "Demo.Api.dll");
            var releaseDll = Path.Combine(rel, "Demo.Api.dll");
            File.WriteAllBytes(releaseDll, [1]);
            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(debugDll, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(releaseDll, now);

            var (_, path) = BuildFreshness.BuildOutput(projectDir);
            Assert.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SameProject_MatchesCsprojAndDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "x");
        var dir = Path.Combine(root, "src", "Demo.Api");
        var csproj = Path.Combine(dir, "Demo.Api.csproj");
        Assert.True(BuildFreshness.SameProject(root, "src/Demo.Api", dir));
        Assert.True(BuildFreshness.SameProject(root, "src/Demo.Api", csproj));
    }

    [Theory]
    [InlineData("queued", "等待", "queued")]
    [InlineData("building", "編譯中", "building")]
    [InlineData("failed", "失敗", "failed")]
    public void ActivityOverridesFreshnessBadge(string activity, string badge, string row)
    {
        var state = new BuildState("id", "n", "stale", "p", Activity: activity);
        Assert.Equal(badge, BuildFreshness.BadgeText(state));
        Assert.Equal(row, BuildFreshness.RowKind(state));
    }

    [Fact]
    public void FormatAgo_UsesRelativeChinese()
    {
        var now = DateTimeOffset.Parse("2026-08-25T12:00:00Z");
        Assert.Equal("剛剛", BuildFreshness.FormatAgo(now.AddSeconds(-10), now));
        Assert.Equal("5 分鐘前", BuildFreshness.FormatAgo(now.AddMinutes(-5), now));
    }

    static ProjectInfo DemoApiInfo() => new(
        RelDir: "src/Demo.Api",
        Name: "Demo.Api",
        Csproj: "Demo.Api.csproj",
        Sdk: "Microsoft.NET.Sdk.Web",
        OutputType: "Exe",
        IsExecutable: true,
        IsWeb: true,
        IsWebApi: true,
        IsTest: false,
        Ports: [8080],
        ApplicationUrls: ["http://localhost:8080"],
        LaunchUrl: "",
        Group: "Demo",
        Language: "C#");

    static string CreateProject(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-fresh-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", name);
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        Directory.CreateDirectory(Path.Combine(proj, "bin", "Debug", "net8.0"));
        File.WriteAllText(Path.Combine(proj, name + ".csproj"), """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(proj, "Program.cs"), "Console.WriteLine();");
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
        { "profiles": { "http": { "applicationUrl": "http://localhost:8080" } } }
        """);
        File.WriteAllText(Path.Combine(proj, "appsettings.json"), "{ }");
        File.WriteAllBytes(Path.Combine(proj, "bin", "Debug", "net8.0", name + ".dll"), [0]);
        return root;
    }

    static void StampSources(string projectDir, DateTime utc)
    {
        foreach (var path in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(p => p is "bin" or "obj"))
                continue;
            File.SetLastWriteTimeUtc(path, utc);
        }
    }
}
