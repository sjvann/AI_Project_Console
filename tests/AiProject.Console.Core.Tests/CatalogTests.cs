using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Scan;

namespace AiProject.Console.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void Slug_NormalizesName()
    {
        Assert.Equal("hub-api", ServiceCatalogBuilder.Slug("Hub Api"));
        Assert.Equal("service", ServiceCatalogBuilder.Slug("@@@"));
    }

    [Fact]
    public void ParseGithubSlug_FromHttpsAndSsh()
    {
        var https = GithubConfigResolver.ParseSlug("https://github.com/my-org/my-repo.git");
        Assert.Equal("my-org", https.Owner);
        Assert.Equal("my-repo", https.Repo);
        var ssh = GithubConfigResolver.ParseSlug("git@github.com:acme/app.git");
        Assert.Equal("acme", ssh.Owner);
        Assert.Equal("app", ssh.Repo);
    }

    [Fact]
    public void BuildCatalog_FromManifest()
    {
        var root = CreateTempProject();
        try
        {
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("Demo", catalog.Name);
            Assert.Equal(2, catalog.Services.Count);
            Assert.Equal("api", catalog.Services[0].Id);
            Assert.Equal("web", catalog.Services[1].Id);
            Assert.Equal("api", catalog.Services[1].HostedBy);
            Assert.Equal(new[] { "api" }, catalog.StartOrder);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deploy_InfersGcpFromLegacyBlock()
    {
        var root = CreateTempProject();
        try
        {
            File.WriteAllText(Path.Combine(root, "ai-project.json"), """
            {
              "name": "Demo",
              "gcp": { "projectId": "p1", "zone": "asia-east1-b", "instance": "vm1" }
            }
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var cfg = DeployConfigResolver.FromManifest(catalog);
            Assert.Equal(DeployTargets.Gcp, cfg.Target);
            Assert.Equal("p1", cfg.Gcp.ProjectId);
            Assert.True(cfg.Gcp.IsComplete());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scanner_ReadsLaunchSettings()
    {
        var root = CreateTempProject();
        try
        {
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.Single(scan.Projects);
            var p = scan.Projects[0];
            Assert.True(p.IsWeb);
            Assert.Contains(8080, p.Ports);
            Assert.Contains("http://localhost:8080", p.ApplicationUrls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateTempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", "Demo.Api");
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        File.WriteAllText(Path.Combine(proj, "Demo.Api.csproj"), """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <OutputType>Exe</OutputType>
          </PropertyGroup>
        </Project>
        """);
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), """
        {
          "profiles": {
            "http": { "applicationUrl": "http://localhost:8080", "launchUrl": "scalar" }
          }
        }
        """);
        File.WriteAllText(Path.Combine(root, "ai-project.json"), """
        {
          "name": "Demo",
          "services": [
            { "id": "api", "label": "Api", "project": "src/Demo.Api", "port": 8080, "group": "Demo" },
            { "id": "web", "label": "Web", "project": "src/Demo.Api", "port": 8080, "hostedBy": "api", "openUrl": "http://localhost:8080/" }
          ],
          "startOrder": ["api"],
          "frontend": "web"
        }
        """);
        return root;
    }
}
