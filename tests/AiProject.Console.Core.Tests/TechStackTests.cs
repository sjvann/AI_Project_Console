using AiProject.Console.Core.Build;
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
    public void PlanBuild_NodePackageJson_UsesNpmBuild()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-stack-node-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "package.json"), """
            { "name": "demo", "scripts": { "build": "vite build" } }
            """);
            var plan = StackCommands.PlanBuild(dir, dir);
            Assert.DoesNotContain("dotnet build", plan.Display, StringComparison.OrdinalIgnoreCase);
            if (plan.MissingToolIds.Count == 0)
                Assert.Contains("build", plan.Display, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PlanBuild_JsFolderWithoutManifest_DoesNotDotnetBuild()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai-stack-js-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "app.js"), "console.log(1)\n");
            File.WriteAllText(Path.Combine(dir, "main.js"), "console.log(2)\n");
            var plan = StackCommands.PlanBuild(dir, dir);
            Assert.DoesNotContain("dotnet", plan.Display, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(plan.MissingToolIds);
            Assert.Contains("無需編譯", plan.Display);
            Assert.Equal("", plan.FileName);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void PlanBuild_WwwrootJs_BuildsParentCsproj()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-www-build-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(root, "src", "Demo.Web");
        var js = Path.Combine(web, "wwwroot", "js");
        Directory.CreateDirectory(js);
        try
        {
            var csproj = Path.Combine(web, "Demo.Web.csproj");
            File.WriteAllText(csproj, """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(js, "app.js"), "console.log(1)\n");
            var plan = StackCommands.PlanBuild(root, js);
            Assert.Contains("dotnet build", plan.Display, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(plan.Arguments, a =>
                string.Equals(Path.GetFullPath(a), Path.GetFullPath(csproj), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(Path.GetFullPath(web), Path.GetFullPath(plan.WorkingDirectory));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlanBuild_TemplatesPublic_DoesNotDotnetBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-tpl-build-" + Guid.NewGuid().ToString("N"));
        var pub = Path.Combine(root, "templates", "public");
        Directory.CreateDirectory(pub);
        try
        {
            File.WriteAllText(Path.Combine(root, "Demo.slnx"), "{ }\n");
            File.WriteAllText(Path.Combine(pub, "main.js"), "console.log(1)\n");
            File.WriteAllText(Path.Combine(pub, "main.css"), "body{}\n");
            var plan = StackCommands.PlanBuild(root, pub);
            Assert.DoesNotContain("dotnet", plan.Display, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("無需編譯", plan.Display);
            Assert.Equal("", plan.FileName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PlanBuild_UiFolderWithoutEntry_DoesNotDotnetBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-ui-build-" + Guid.NewGuid().ToString("N"));
        var ui = Path.Combine(root, "ui");
        Directory.CreateDirectory(ui);
        try
        {
            File.WriteAllText(Path.Combine(root, "Demo.slnx"), "{ }\n");
            foreach (var name in new[] { "buttons.py", "dialog.py", "bar.py", "help.py", "settings.py", "__init__.py" })
                File.WriteAllText(Path.Combine(ui, name), "x = 1\n");
            var plan = StackCommands.PlanBuild(root, ui);
            Assert.DoesNotContain("dotnet", plan.Display, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("無需編譯", plan.Display);
            Assert.Equal("", plan.FileName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("src/AiProject.Console.App/wwwroot/js")]
    [InlineData("templates/public")]
    [InlineData("ui")]
    public async Task Build_ThisRepoStaticFolders_ExitsZero(string rel)
    {
        var root = FindRepoRoot();
        var target = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(target), target);
        var (code, log) = await BuildRunner.BuildAsync(root, target, null);
        Assert.True(code == 0, log);
    }

    [Fact]
    public void ScanWorkspace_AiProjectConsoleRepo_OmitsStaticAndUiFolders()
    {
        var root = FindRepoRoot();
        var scan = ProjectScanner.ScanWorkspace(root);
        Assert.DoesNotContain(scan.Projects, p =>
            p.RelDir.Contains("wwwroot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scan.Projects, p =>
            p.RelDir.Replace('\\', '/').Equals("ui", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scan.Projects, p =>
            p.RelDir.Replace('\\', '/').Contains("templates/public", StringComparison.OrdinalIgnoreCase));
    }

    static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "AiProject.Console.slnx")))
                return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent))
                break;
            dir = parent;
        }
        throw new DirectoryNotFoundException("找不到含 AiProject.Console.slnx 的倉根目錄");
    }

    [Fact]
    public void ScanWorkspace_WwwrootJs_IsNotASeparateProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-www-" + Guid.NewGuid().ToString("N"));
        var web = Path.Combine(root, "src", "Demo.Web");
        var js = Path.Combine(web, "wwwroot", "js");
        Directory.CreateDirectory(js);
        try
        {
            File.WriteAllText(Path.Combine(web, "Demo.Web.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(js, "app.js"), "console.log(1)\n");
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.DoesNotContain(scan.Projects, p => p.RelDir.Contains("wwwroot", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(scan.Projects, p => p.Name == "Demo.Web" && p.StackId == "dotnet");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanWorkspace_StaticMainJs_IsNotANodeProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-tpl-" + Guid.NewGuid().ToString("N"));
        var pub = Path.Combine(root, "templates", "public");
        Directory.CreateDirectory(pub);
        try
        {
            File.WriteAllText(Path.Combine(pub, "main.js"), "console.log(1)\n");
            File.WriteAllText(Path.Combine(pub, "main.css"), "body{}\n");
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.DoesNotContain(scan.Projects, p => p.StackId == "node");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanWorkspace_PythonUiFolderWithoutEntry_IsNotAProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-ui-" + Guid.NewGuid().ToString("N"));
        var ui = Path.Combine(root, "ui");
        Directory.CreateDirectory(ui);
        try
        {
            foreach (var name in new[] { "buttons.py", "dialog.py", "bar.py", "help.py", "settings.py", "__init__.py" })
                File.WriteAllText(Path.Combine(ui, name), "x = 1\n");
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.DoesNotContain(scan.Projects, p => p.RelDir.Equals("ui", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ScanWorkspace_PythonBesideSolution_IsNotALooseProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-stack-sln-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "Demo"));
        try
        {
            File.WriteAllText(Path.Combine(root, "app.py"), "print(1)\n");
            File.WriteAllText(Path.Combine(root, "Demo.slnx"), "{ }\n");
            File.WriteAllText(Path.Combine(root, "src", "Demo", "Demo.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            var scan = ProjectScanner.ScanWorkspace(root);
            Assert.DoesNotContain(scan.Projects, p => p.StackId == "python");
            Assert.Contains(scan.Projects, p => p.Name == "Demo" && p.StackId == "dotnet");
        }
        finally
        {
            Directory.Delete(root, true);
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
