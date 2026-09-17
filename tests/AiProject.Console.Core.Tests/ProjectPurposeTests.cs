using System.Text.Json.Nodes;
using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Tests;

public class ProjectPurposeTests
{
    [Fact]
    public void Fill_LibraryCsprojDescription_IsKept()
    {
        var root = NewRoot();
        try
        {
            WriteCsproj(root, "src/Demo.Core", "Demo.Core", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Description>核心領域模型</Description>
              </PropertyGroup>
            </Project>
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var project = Assert.Single(catalog.Projects);
            Assert.Equal("核心領域模型", project.Description);
            Assert.Equal(ProjectPurpose.SourceProjectFile, project.DescriptionSource);
            Assert.Empty(catalog.Services);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Fill_PackageDescription_WhenDescriptionMissing()
    {
        var root = NewRoot();
        try
        {
            WriteCsproj(root, "src/Demo.Lib", "Demo.Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <PackageDescription>套件說明當用途</PackageDescription>
              </PropertyGroup>
            </Project>
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            Assert.Equal("套件說明當用途", Assert.Single(catalog.Projects).Description);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Fill_PackageJsonDescription()
    {
        var root = NewRoot();
        try
        {
            var dir = Path.Combine(root, "web");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "package.json"), """
            { "name": "demo-web", "description": "前端工作台" }
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var project = Assert.Single(catalog.Projects);
            Assert.Equal("前端工作台", project.Description);
            Assert.Equal(ProjectPurpose.SourcePackageJson, project.DescriptionSource);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Fill_ReadmeFirstParagraph_WhenNoProjectDescription()
    {
        var root = NewRoot();
        try
        {
            WriteCsproj(root, "src/Demo.Lib", "Demo.Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(root, "src", "Demo.Lib", "README.md"), """
            # Demo.Lib

            這是給報表用的共用函式庫。

            後面還有很長的安裝說明。
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var project = Assert.Single(catalog.Projects);
            Assert.Equal("這是給報表用的共用函式庫。", project.Description);
            Assert.Equal(ProjectPurpose.SourceReadme, project.DescriptionSource);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Fill_ManifestProjects_WinsOverCsproj()
    {
        var root = NewRoot();
        try
        {
            WriteCsproj(root, "src/Demo.Lib", "Demo.Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Description>掃描到的用途</Description>
              </PropertyGroup>
            </Project>
            """);
            File.WriteAllText(Path.Combine(root, "ai-project.json"), """
            {
              "name": "Demo",
              "projects": [
                { "path": "src/Demo.Lib", "description": "清單上的函式庫用途" }
              ]
            }
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var project = Assert.Single(catalog.Projects);
            Assert.Equal("清單上的函式庫用途", project.Description);
            Assert.Equal(ProjectPurpose.SourceManifest, project.DescriptionSource);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Build_WritesProductPurposesDocument()
    {
        var root = NewRoot();
        try
        {
            WriteCsproj(root, "src/Demo.Lib", "Demo.Lib", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <Description>核心領域模型</Description>
              </PropertyGroup>
            </Project>
            """);
            WriteCsproj(root, "src/Demo.Empty", "Demo.Empty", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
            var catalog = ServiceCatalogBuilder.Build(root);
            var path = Path.Combine(root, ".ai_project", "product-purposes.md");
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("核心領域模型", text);
            Assert.Contains("Demo.Empty", text);
            Assert.Contains("尚未提供", text);
            Assert.Equal(text, ProjectPurpose.Render(catalog.Projects, catalog.Name));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FirstParagraph_SkipsHeadingAndBadges()
    {
        var text = ProjectPurpose.FirstParagraph("""
        # Title

        ![build](https://example.com/badge.svg)

        第一段用途說明。

        第二段不該出現。
        """);
        Assert.Equal("第一段用途說明。", text);
    }

    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-purpose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static void WriteCsproj(string root, string relDir, string name, string csproj)
    {
        var dir = Path.Combine(root, relDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"), csproj);
    }

    static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { /* temp */ }
    }
}
