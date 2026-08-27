using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Scan;

namespace AiProject.Console.Core.Tests;

public class CatalogRefreshTests
{
    [Fact]
    public void Diff_NoChange_ReportsUnchanged()
    {
        var catalog = Catalog("Demo.Api");
        var diff = CatalogRefresh.Diff(catalog, catalog);
        Assert.False(diff.HasChanges);
        Assert.Equal("已重新掃描，專案與服務沒有增減", diff.Format());
    }

    [Fact]
    public void Diff_AddedProjectAndService()
    {
        var before = Catalog("Demo.Api");
        var after = Catalog("Demo.Api", "Demo.Web");
        var diff = CatalogRefresh.Diff(before, after);
        Assert.True(diff.HasChanges);
        Assert.Equal(["Demo.Web"], diff.AddedProjects);
        Assert.Empty(diff.RemovedProjects);
        Assert.Equal(["Demo.Web"], diff.AddedServices);
        Assert.Empty(diff.RemovedServices);
        Assert.Contains("新增 1 個專案（Demo.Web）", diff.Format());
        Assert.Contains("新增 1 個服務（Demo.Web）", diff.Format());
    }

    [Fact]
    public void Diff_RemovedProjectAndService()
    {
        var before = Catalog("Demo.Api", "Demo.Web");
        var after = Catalog("Demo.Api");
        var diff = CatalogRefresh.Diff(before, after);
        Assert.Equal(["Demo.Web"], diff.RemovedProjects);
        Assert.Equal(["Demo.Web"], diff.RemovedServices);
        Assert.Contains("移除 1 個專案（Demo.Web）", diff.Format());
    }

    [Fact]
    public void Diff_IgnoresSameProjectDifferentScanOrder()
    {
        var a = Catalog("B.Lib", "A.Lib");
        var b = Catalog("A.Lib", "B.Lib");
        var diff = CatalogRefresh.Diff(a, b);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Build_FindsProjectAddedAfterFirstScan()
    {
        var root = CreateScanRoot("Demo.Api");
        try
        {
            var before = ServiceCatalogBuilder.Build(root);
            Assert.Single(before.Projects);
            Assert.Equal("Demo.Api", before.Projects[0].Name);

            var web = Path.Combine(root, "src", "Demo.Web");
            Directory.CreateDirectory(Path.Combine(web, "Properties"));
            File.WriteAllText(Path.Combine(web, "Demo.Web.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(web, "Properties", "launchSettings.json"), """
                { "profiles": { "http": { "applicationUrl": "http://localhost:8081" } } }
                """);

            var after = ServiceCatalogBuilder.Build(root);
            var diff = CatalogRefresh.Diff(before, after);
            Assert.Equal(2, after.Projects.Count);
            Assert.Contains(after.Projects, p => p.Name == "Demo.Web");
            Assert.Contains(after.Services, s => s.Label == "Demo.Web");
            Assert.Equal(["Demo.Web"], diff.AddedProjects);
            Assert.Contains("Demo.Web", diff.AddedServices);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static ProjectCatalog Catalog(params string[] names)
    {
        var projects = names.Select(name => new ProjectInfo(
            RelDir: "src/" + name,
            Name: name,
            Csproj: name + ".csproj",
            Sdk: "Microsoft.NET.Sdk.Web",
            OutputType: "Exe",
            IsExecutable: true,
            IsWeb: true,
            IsWebApi: false,
            IsTest: false,
            Ports: [8080],
            ApplicationUrls: ["http://localhost:8080"],
            LaunchUrl: "",
            Group: "src",
            Language: "C#",
            IsUi: name.Contains("Web", StringComparison.OrdinalIgnoreCase))).ToList();
        var services = names.Select(name => new ServiceEntry(
            Id: ServiceCatalogBuilder.Slug(name),
            Label: name,
            Stem: name,
            Project: "src/" + name,
            Port: 8080,
            Health: "http://127.0.0.1:8080/health",
            OpenUrl: "http://localhost:8080/",
            Group: "src",
            Source: "scan")).ToList();
        return new ProjectCatalog
        {
            Root = @"E:\demo",
            Name = "Demo",
            Services = services,
            Projects = projects,
            StartOrder = services.Select(s => s.Id).ToList(),
            Frontend = services.FirstOrDefault()?.Id ?? "",
            Manifest = [],
            Scan = new ScanResult(@"E:\demo", projects),
            Summary = $"{projects.Count} 專案",
        };
    }

    static string CreateScanRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-refresh-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", name);
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        File.WriteAllText(Path.Combine(proj, name + ".csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
            { "profiles": { "http": { "applicationUrl": "http://localhost:8080" } } }
            """);
        return root;
    }
}
