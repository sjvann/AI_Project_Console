using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Util;
using System.Text;
using System.Text.Json.Nodes;

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
    public void NormalizeCloneUrl_AcceptsSlugAndUrls()
    {
        Assert.Equal("https://github.com/acme/app.git", GitHubService.NormalizeCloneUrl("acme/app"));
        Assert.Equal("https://github.com/acme/app.git", GitHubService.NormalizeCloneUrl("github.com/acme/app"));
        Assert.Equal("https://github.com/acme/app.git", GitHubService.NormalizeCloneUrl("https://github.com/acme/app.git"));
        Assert.Equal("git@github.com:acme/app.git", GitHubService.NormalizeCloneUrl("git@github.com:acme/app.git"));
        Assert.Equal("app", GitHubService.SuggestFolderName("acme/app"));
        Assert.Equal("", GitHubService.NormalizeCloneUrl("not-a-repo"));
    }

    [Fact]
    public void JsonUtil_SaveObject_DoesNotRequireExternalTypeInfoResolver()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-console-json-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            JsonUtil.SaveObject(path, new JsonObject { ["recentProjects"] = new JsonArray { @"E:\demo" } });
            var loaded = JsonUtil.LoadObject(path);
            Assert.Equal(@"E:\demo", JsonUtil.Str(loaded["recentProjects"]?[0]));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task LogFileUtil_AllowsConcurrentReadWhileWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-console-log-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            File.WriteAllText(path, "");
            using var stream = LogFileUtil.OpenAppend(path);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            var errors = new List<Exception>();
            var write = Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    try { lock (writer) writer.WriteLine("line " + i); }
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                }
            });
            var read = Task.Run(() =>
            {
                for (var i = 0; i < 200; i++)
                {
                    try { _ = LogFileUtil.ReadAllBytes(path); }
                    catch (Exception ex) { lock (errors) errors.Add(ex); }
                }
            });
            await Task.WhenAll(write, read);
            Assert.Empty(errors);
            var text = Encoding.UTF8.GetString(LogFileUtil.ReadAllBytes(path));
            Assert.Contains("line 0", text);
            Assert.Contains("line 199", text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
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
