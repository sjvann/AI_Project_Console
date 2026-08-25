using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.Deploy;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Util;
using System.Text;
using System.Text.Json.Nodes;
using ServiceEntry = AiProject.Console.Core.ServiceEntry;

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

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3, "", "v")]
    [InlineData("1.2.3", 1, 2, 3, "", "")]
    [InlineData("v2.0", 2, 0, 0, "", "v")]
    [InlineData("1.4.0-rc.1", 1, 4, 0, "rc.1", "")]
    [InlineData("v0.3.1-beta.2", 0, 3, 1, "beta.2", "v")]
    public void ReleaseVersion_ParsesSemVer(string text, int maj, int min, int pat, string pre, string prefix)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var ver));
        Assert.Equal(maj, ver.Major);
        Assert.Equal(min, ver.Minor);
        Assert.Equal(pat, ver.Patch);
        Assert.Equal(pre, ver.PreRelease);
        Assert.Equal(prefix, ver.Prefix);
    }

    [Fact]
    public void ReleaseVersion_BumpsAndSuggests()
    {
        Assert.True(ReleaseVersion.TryParse("v1.2.3", out var stable));
        Assert.Equal("v1.2.4", ReleaseVersion.Bump(stable, "patch").ToTag());
        Assert.Equal("v1.3.0", ReleaseVersion.Bump(stable, "minor").ToTag());
        Assert.Equal("v2.0.0", ReleaseVersion.Bump(stable, "major").ToTag());

        Assert.True(ReleaseVersion.TryParse("v1.2.3-rc.1", out var pre));
        Assert.Equal("v1.2.3", ReleaseVersion.Bump(pre, "patch").ToTag());
        Assert.Equal("v1.3.0", ReleaseVersion.Bump(pre, "minor").ToTag());

        Assert.Equal("v1.2.4", ReleaseVersion.SuggestNext("v1.2.3"));
        Assert.Equal("v0.1.0", ReleaseVersion.SuggestNext(""));
        Assert.Equal("v1.2.3", ReleaseVersion.NormalizeTag("1.2.3"));
        Assert.Equal("v1.2.3", ReleaseVersion.NormalizeTag("v1.2.3"));
        Assert.True(ReleaseVersion.IsValidTag("v1.2.3"));
        Assert.False(ReleaseVersion.IsValidTag("v1 2 3"));
        Assert.False(ReleaseVersion.IsValidTag(""));
        Assert.True(ReleaseVersion.IsNewer("v0.3.3", "0.3.2"));
        Assert.False(ReleaseVersion.IsNewer("v0.3.2", "0.3.2"));
        Assert.False(ReleaseVersion.IsNewer("v0.3.1", "0.3.2"));
        Assert.True(ReleaseVersion.IsNewer("1.0.0", "1.0.0-rc.1"));
        Assert.False(ReleaseVersion.IsNewer("1.0.0-rc.1", "1.0.0"));
    }

    [Fact]
    public void ReleaseVersion_SuggestTag_PrefersGithubThenExisting()
    {
        Assert.Equal("v0.3.2", ReleaseVersion.SuggestTag("v0.3.1", "v0.3.1", "0.3.1"));
        Assert.Equal("v0.3.1", ReleaseVersion.SuggestTag("", "v0.3.1", "0.2.0"));
        Assert.Equal("v0.2.0", ReleaseVersion.SuggestTag("", "", "0.2.0"));
        Assert.Equal("v0.1.0", ReleaseVersion.SuggestTag("", "", ""));
    }

    [Fact]
    public void ReleaseVersion_DetectsFromProjectFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-ver-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Version>1.4.2</Version>
              </PropertyGroup>
            </Project>
            """);
            Assert.Equal("1.4.2", ReleaseVersion.DetectProjectVersion(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public void BuildCatalog_ScansProductLineRoots()
    {
        var parent = Path.Combine(Path.GetTempPath(), "ai-console-lines-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(parent, "AION");
        var ledger = Path.Combine(parent, "LedgerRepo", "src", "Ledger.Lib");
        var weave = Path.Combine(parent, "WeaveRepo", "src", "Weave.Api");
        try
        {
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(ledger);
            Directory.CreateDirectory(weave);
            File.WriteAllText(Path.Combine(ledger, "Ledger.Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(weave, "Weave.Api.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(workspace, "ai-project.json"), """
            {
              "name": "AION",
              "productLines": [
                { "id": "ledger", "label": "Ledger", "root": "../LedgerRepo" },
                { "id": "weave", "label": "Weave", "root": "../WeaveRepo" }
              ],
              "services": [
                { "id": "api", "label": "Api", "project": "../WeaveRepo/src/Weave.Api", "port": 8088, "group": "Weave" }
              ]
            }
            """);
            var catalog = ServiceCatalogBuilder.Build(workspace);
            Assert.Equal(2, catalog.Projects.Count);
            Assert.Contains(catalog.Projects, p => p.Name == "Ledger.Lib" && p.Group == "Ledger");
            Assert.Contains(catalog.Projects, p => p.Name == "Weave.Api" && p.Group == "Weave");
            Assert.Contains(catalog.Projects, p => p.RelDir.Replace('\\', '/') == "../LedgerRepo/src/Ledger.Lib");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ReadProductLines_InfersFromSiblingServices()
    {
        var manifest = JsonNode.Parse("""
        {
          "services": [
            { "id": "fhir-host", "project": "../FHIR-ProfileServer/src/Fhir.Server.Host", "group": "Ledger" },
            { "id": "ezie-api", "project": "../AI_EZIE/src/Ezie.Api", "group": "Weave" },
            { "id": "ezie-web", "project": "../AI_EZIE/src/Ezie.Web", "group": "Weave" }
          ]
        }
        """)!.AsObject();
        var services = new[]
        {
            new ServiceEntry("fhir-host", "Ledger", "Fhir.Server.Host", "../FHIR-ProfileServer/src/Fhir.Server.Host", 5080, "", "", "Ledger"),
            new ServiceEntry("ezie-api", "Api", "Ezie.Api", "../AI_EZIE/src/Ezie.Api", 8088, "", "", "Weave"),
            new ServiceEntry("ezie-web", "Web", "Ezie.Web", "../AI_EZIE/src/Ezie.Web", 8091, "", "", "Weave"),
        };
        var lines = ServiceCatalogBuilder.ReadProductLines(manifest, services);
        Assert.Equal(2, lines.Count);
        Assert.Equal("../FHIR-ProfileServer", lines[0].Root);
        Assert.Equal("Ledger", lines[0].Label);
        Assert.Equal("../AI_EZIE", lines[1].Root);
        Assert.Equal("Weave", lines[1].Label);
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
            Assert.Equal("scripts/ensure-api.ps1", catalog.Services[0].PreStart);
            Assert.Null(catalog.Services[1].PreStart);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvePreStartPath_StaysInsideRoot()
    {
        var root = CreateTempProject();
        try
        {
            var script = Path.Combine(root, "scripts", "ensure-api.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            File.WriteAllText(script, "exit 0");
            Assert.Equal(
                Path.GetFullPath(script),
                ProcessSupervisor.ResolvePreStartPath(root, "scripts/ensure-api.ps1"));
            Assert.Null(ProcessSupervisor.ResolvePreStartPath(root, "../escape.ps1"));
            Assert.Null(ProcessSupervisor.ResolvePreStartPath(root, "missing.ps1"));
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

    [Theory]
    [InlineData("App.csproj", "C#")]
    [InlineData("Lib.fsproj", "F#")]
    [InlineData("App.vbproj", "VB.NET")]
    [InlineData("Native.vcxproj", "C++")]
    [InlineData("Web.esproj", "JavaScript")]
    [InlineData("App.pyproj", "Python")]
    [InlineData("unknown.xyz", "")]
    public void Scanner_DetectsLanguageFromProjectFile(string file, string expected)
    {
        Assert.Equal(expected, ProjectScanner.DetectLanguage(file));
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
            Assert.True(p.IsWebApi);
            Assert.Equal("scalar", p.LaunchUrl);
            Assert.Contains(8080, p.Ports);
            Assert.Contains("http://localhost:8080", p.ApplicationUrls);
            Assert.Equal("C#", p.Language);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCatalog_WebApiDefaultsOpenUrlToScalar()
    {
        var root = CreateScanOnlyProject("Demo.Api", """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.0" />
          </ItemGroup>
        </Project>
        """, launchUrl: "");
        try
        {
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Single(catalog.Services);
            Assert.Equal("http://localhost:8080/scalar", catalog.Services[0].OpenUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCatalog_WebUiKeepsRootOpenUrl()
    {
        var root = CreateScanOnlyProject("Demo.Web", """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
            <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
          </PropertyGroup>
        </Project>
        """, launchUrl: "", extraFile: ("Home.razor", "<h1>Hi</h1>"));
        try
        {
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Single(catalog.Services);
            Assert.False(catalog.Scan.Projects[0].IsWebApi);
            Assert.Equal("http://localhost:8080/", catalog.Services[0].OpenUrl);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BuildCatalog_ExplicitLaunchUrlWinsOverScalarDefault()
    {
        var root = CreateScanOnlyProject("Demo.Api", """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """, launchUrl: "swagger");
        try
        {
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("http://localhost:8080/swagger", catalog.Services[0].OpenUrl);
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
            { "id": "api", "label": "Api", "project": "src/Demo.Api", "port": 8080, "group": "Demo", "preStart": "scripts/ensure-api.ps1" },
            { "id": "web", "label": "Web", "project": "src/Demo.Api", "port": 8080, "hostedBy": "api", "openUrl": "http://localhost:8080/" }
          ],
          "startOrder": ["api"],
          "frontend": "web"
        }
        """);
        return root;
    }

    static string CreateScanOnlyProject(string name, string csproj, string launchUrl, (string Name, string Content)? extraFile = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-test-" + Guid.NewGuid().ToString("N"));
        var proj = Path.Combine(root, "src", name);
        Directory.CreateDirectory(Path.Combine(proj, "Properties"));
        File.WriteAllText(Path.Combine(proj, name + ".csproj"), csproj);
        var launch = string.IsNullOrEmpty(launchUrl)
            ? """
            {
              "profiles": {
                "http": { "applicationUrl": "http://localhost:8080" }
              }
            }
            """
            : $$"""
            {
              "profiles": {
                "http": { "applicationUrl": "http://localhost:8080", "launchUrl": "{{launchUrl}}" }
              }
            }
            """;
        File.WriteAllText(Path.Combine(proj, "Properties", "launchSettings.json"), launch);
        if (extraFile is { } extra)
            File.WriteAllText(Path.Combine(proj, extra.Name), extra.Content);
        return root;
    }
}
