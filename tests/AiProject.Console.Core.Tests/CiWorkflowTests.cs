using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class CiWorkflowTests
{
    [Fact]
    public void ListFiles_ClassifiesBuildTestVsDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-ci-wf-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(root, ".github", "workflows");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "docs.yml"), "name: docs\n");
            Assert.True(CiWorkflow.HasAny(root));
            Assert.False(CiWorkflow.HasBuildTest(root));
            Assert.Contains("僅文件", CiWorkflow.DoctorLine(root));
            Assert.Equal("文件", CiWorkflow.Describe(root).Badge);

            File.WriteAllText(Path.Combine(dir, "ci.yml"), "name: CI\n");
            Assert.True(CiWorkflow.HasBuildTest(root));
            Assert.Contains("ci.yml", CiWorkflow.DoctorLine(root));
            Assert.Equal("有", CiWorkflow.Describe(root).Badge);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void EmptyRoot_HasNoWorkflows()
    {
        Assert.False(CiWorkflow.HasAny(null));
        Assert.Equal("CI workflow: 無", CiWorkflow.DoctorLine(null));
        var root = Path.Combine(Path.GetTempPath(), "ai-ci-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(CiWorkflow.HasAny(root));
            Assert.Equal("無", CiWorkflow.Describe(root).Badge);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Template_UsesSolutionAndGlobalJson()
    {
        var yaml = CiWorkflow.Template("Demo.slnx", useGlobalJson: true, defaultBranch: "develop");
        Assert.Contains("branches: [develop]", yaml);
        Assert.Contains("global-json-file: global.json", yaml);
        Assert.Contains("dotnet restore Demo.slnx", yaml);
        Assert.Contains("dotnet test Demo.slnx", yaml);
        Assert.Contains("permissions:", yaml);
        Assert.Contains("contents: read", yaml);
        Assert.DoesNotContain("pages: write", yaml);
    }

    [Fact]
    public void Ensure_WritesOnceAndDoesNotOverwrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-ci-ensure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Demo.sln"), "");
        File.WriteAllText(Path.Combine(root, "global.json"), """{ "sdk": { "version": "10.0.100" } }""");
        try
        {
            var first = CiWorkflow.Ensure(root, "main");
            Assert.True(first.Created);
            Assert.True(File.Exists(first.Path));
            var text = File.ReadAllText(first.Path);
            Assert.Contains("dotnet restore Demo.sln", text);
            Assert.Contains("global-json-file: global.json", text);
            File.WriteAllText(first.Path, "name: keep\n");
            var second = CiWorkflow.Ensure(root);
            Assert.False(second.Created);
            Assert.Equal("name: keep\n", File.ReadAllText(second.Path));
            Assert.Equal("10.0.x", CiWorkflow.DetectDotnetVersion(root));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }
}
