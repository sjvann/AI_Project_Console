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
}
