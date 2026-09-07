using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Scan;

namespace AiProject.Console.Core.Tests;

public class AppIconLocatorTests
{
    static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public void FindAbsolute_PrefersApplicationIcon()
    {
        var dir = CreateDir();
        try
        {
            var assets = Path.Combine(dir, "Assets");
            Directory.CreateDirectory(assets);
            var appIco = Path.Combine(assets, "app.ico");
            File.WriteAllBytes(appIco, TinyPng);
            var www = Path.Combine(dir, "wwwroot");
            Directory.CreateDirectory(www);
            File.WriteAllBytes(Path.Combine(www, "favicon.png"), TinyPng);

            var found = AppIconLocator.FindAbsolute(dir, Path.Combine("Assets", "app.ico"));
            Assert.Equal(appIco, found);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FindAbsolute_UsesWwwrootFaviconWhenNoApplicationIcon()
    {
        var dir = CreateDir();
        try
        {
            var www = Path.Combine(dir, "wwwroot");
            Directory.CreateDirectory(www);
            var favicon = Path.Combine(www, "favicon.png");
            File.WriteAllBytes(favicon, TinyPng);

            Assert.Equal(favicon, AppIconLocator.FindAbsolute(dir));
            var workspace = Path.GetFullPath(Path.Combine(dir, "..", ".."));
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", AppIconLocator.FindRel(workspace, dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryDataUrl_EncodesPng()
    {
        var dir = CreateDir();
        try
        {
            var png = Path.Combine(dir, "favicon.png");
            File.WriteAllBytes(png, TinyPng);
            var url = AppIconLocator.TryDataUrl(png);
            Assert.NotNull(url);
            Assert.StartsWith("data:image/png;base64,", url);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceIcon_PrefersWebUiOverDesktop()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteWeb(root, "Demo.Web", withIcon: true);
            WriteDesktop(root, "Demo.App", withIcon: true);
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", catalog.Scan.Projects.First(p => p.Name == "Demo.Web").IconPath);
            Assert.False(string.IsNullOrEmpty(catalog.Scan.Projects.First(p => p.Name == "Demo.App").IconPath));
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", AppIconLocator.WorkspaceIcon(catalog));
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", catalog.Services.First(s => s.Label == "Demo.Web").IconPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceIcon_UsesManifestIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteWeb(root, "Demo.Web", withIcon: true);
            var brand = Path.Combine(root, "brand");
            Directory.CreateDirectory(brand);
            File.WriteAllBytes(Path.Combine(brand, "mark.png"), TinyPng);
            File.WriteAllText(Path.Combine(root, "ai-project.json"), """
            { "name": "Demo", "icon": "brand/mark.png" }
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("brand/mark.png", AppIconLocator.WorkspaceIcon(catalog));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCatalog_FillsServiceIconFromWwwroot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteWeb(root, "Demo.Web", withIcon: true);
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Single(catalog.Services);
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", catalog.Services[0].IconPath);
            var states = AiProject.Console.Core.Build.BuildFreshness.AllProjectBuildStates(catalog);
            Assert.Equal("src/Demo.Web/wwwroot/favicon.png", states[0].IconPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static void WriteWeb(string root, string name, bool withIcon)
    {
        var proj = Path.Combine(root, "src", name);
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        File.WriteAllText(Path.Combine(proj, name + ".csproj"), """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(proj, "Home.razor"), "<h1>Hi</h1>");
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
        { "profiles": { "http": { "applicationUrl": "http://localhost:8080" } } }
        """);
        if (withIcon)
        {
            var www = Path.Combine(proj, "wwwroot");
            Directory.CreateDirectory(www);
            File.WriteAllBytes(Path.Combine(www, "favicon.png"), TinyPng);
        }
    }

    static void WriteDesktop(string root, string name, bool withIcon)
    {
        var proj = Path.Combine(root, "src", name);
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, name + ".csproj"), """
        <Project Sdk="Microsoft.NET.Sdk.Razor">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <ApplicationIcon>Assets\app.ico</ApplicationIcon>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Photino.Blazor" Version="4.0.13" />
          </ItemGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(proj, "Home.razor"), "<h1>Hi</h1>");
        if (withIcon)
        {
            var assets = Path.Combine(proj, "Assets");
            Directory.CreateDirectory(assets);
            File.WriteAllBytes(Path.Combine(assets, "app.ico"), TinyPng);
        }
    }

    static string CreateDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"), "src", "Demo.Web");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
