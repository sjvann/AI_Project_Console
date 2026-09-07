using AiProject.Console.Core.Catalog;
using AiProject.Console.Core.ProcessOps;
using AiProject.Console.Core.Scan;
using AiProject.Console.Core.Tech;

namespace AiProject.Console.Core.Tests;

public class TechStackTests
{
    [Theory]
    [InlineData("package.json", "node", "JavaScript")]
    [InlineData("pyproject.toml", "python", "Python")]
    [InlineData("go.mod", "go", "Go")]
    [InlineData("Cargo.toml", "rust", "Rust")]
    [InlineData("pom.xml", "java-maven", "Java")]
    [InlineData("build.gradle", "java-gradle", "Java")]
    [InlineData("composer.json", "php", "PHP")]
    [InlineData("Gemfile", "ruby", "Ruby")]
    [InlineData("CMakeLists.txt", "cpp-cmake", "C++")]
    [InlineData("pubspec.yaml", "dart", "Dart")]
    [InlineData("App.csproj", "dotnet", "C#")]
    public void MatchFile_RecognizesCommonManifests(string file, string stackId, string language)
    {
        var stack = TechStackCatalog.MatchFile(file);
        Assert.NotNull(stack);
        Assert.Equal(stackId, stack.Id);
        Assert.Equal(language, stack.Language);
    }

    [Fact]
    public void PickManifests_PrefersPyprojectOverRequirements()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-stack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var py = Path.Combine(dir, "pyproject.toml");
            var req = Path.Combine(dir, "requirements.txt");
            File.WriteAllText(py, "[project]\nname='demo'\n");
            File.WriteAllText(req, "flask\n");
            var picked = TechStackCatalog.PickManifests([py, req]);
            Assert.Single(picked);
            Assert.Equal(py, picked[0]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ScanWorkspace_FindsNodeAndPythonProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-scan-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(root, "web");
        var api = Path.Combine(root, "api");
        Directory.CreateDirectory(web);
        Directory.CreateDirectory(api);
        try
        {
            File.WriteAllText(Path.Combine(web, "package.json"), """
            {
              "name": "web-ui",
              "scripts": { "dev": "vite", "build": "vite build" },
              "dependencies": { "react": "18.0.0" }
            }
            """);
            File.WriteAllText(Path.Combine(api, "pyproject.toml"), """
            [project]
            name = "api"
            dependencies = ["fastapi"]
            """);
            File.WriteAllText(Path.Combine(api, "main.py"), "from fastapi import FastAPI\napp = FastAPI()\n");

            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.Equal(2, scan.Projects.Count);
            Assert.Contains(scan.Projects, p => p.StackId == "node" && p.Language == "JavaScript");
            Assert.Contains(scan.Projects, p => p.StackId == "python" && p.Language == "Python");
            Assert.Contains("node", scan.StackIds!);
            Assert.Contains("python", scan.StackIds!);

            var catalog = ServiceCatalogBuilder.Build(root);
            var stacks = TechStackDetector.RequiredStacks(catalog);
            Assert.Contains("node", stacks);
            Assert.Contains("python", stacks);
            var tools = TechStackCatalog.RequiredToolIdsFor(stacks);
            Assert.Contains("node", tools);
            Assert.Contains("python", tools);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanWorkspace_LoosePythonMainBecomesProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-py-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "main.py"), "print('hi')\n");
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.Contains(scan.Projects, p => p.StackId == "python");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanWorkspace_StrayScriptDoesNotBecomeProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-stray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        try
        {
            File.WriteAllText(Path.Combine(root, "scripts", "make-icons.py"), "print(1)\n");
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.DoesNotContain(scan.Projects, p => p.StackId == "python");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlanBuild_Python_UsesCompileallOrReportsMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-stack-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "pyproject.toml"), "[project]\nname='x'\n");
            var plan = StackCommands.PlanBuild(dir, dir);
            if (ToolchainBootstrap.IsInstalled("python"))
            {
                Assert.Empty(plan.MissingToolIds);
                Assert.Contains("compileall", plan.Display);
            }
            else
            {
                Assert.Contains("python", plan.MissingToolIds);
            }
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FormatMissing_NamesTheTool()
    {
        var text = ToolchainBootstrap.FormatMissing(["python"]);
        Assert.Contains("Python", text);
        Assert.Contains("環境體檢", text);
    }

    [Fact]
    public void Doctor_WithPythonProject_ListsToolchainSection()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-doc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "pyproject.toml"), "[project]\nname='demo'\ndependencies=['flask']\n");
            File.WriteAllText(Path.Combine(root, "app.py"), "print(1)\n");
            var catalog = ServiceCatalogBuilder.Build(root);
            var report = DoctorSnapshot.Build(catalog);
            Assert.Contains(report.Sections, s => s.Id == "toolchain");
            var tools = report.Sections.Single(s => s.Id == "toolchain");
            Assert.Contains(tools.Items, i => i.Label.Contains("Python", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
