using AiProject.Console.Core.Build;

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

            Assert.Equal("stale", BuildFreshness.ProjectBuildState(root, DemoApiInfo()).Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
        Group: "Demo");

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
