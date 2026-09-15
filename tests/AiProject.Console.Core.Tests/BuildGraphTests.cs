using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class BuildGraphTests
{
    [Fact]
    public void Plan_CollapsesMultipleDotnetProjects_ToSolution()
    {
        var root = CreateDotnetWorkspace();
        try
        {
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            var plan = BuildGraph.Plan(root, [a, b]);
            Assert.True(plan.CollapsedToSolution);
            Assert.Single(plan.Steps);
            Assert.True(plan.Steps[0].EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || plan.Steps[0].EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, plan.CoveredByStep[plan.Steps[0]].Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_KeepsSingleTarget()
    {
        var root = CreateDotnetWorkspace();
        try
        {
            var a = Path.Combine(root, "A");
            var plan = BuildGraph.Plan(root, [a]);
            Assert.False(plan.CollapsedToSolution);
            Assert.Equal([a], plan.Steps);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Plan_LeavesNonDotnetBesideSolution()
    {
        var root = CreateDotnetWorkspace();
        try
        {
            var web = Path.Combine(root, "web");
            Directory.CreateDirectory(web);
            File.WriteAllText(Path.Combine(web, "package.json"), """{"name":"web"}""");
            var a = Path.Combine(root, "A");
            var b = Path.Combine(root, "B");
            var plan = BuildGraph.Plan(root, [a, b, web]);
            Assert.True(plan.CollapsedToSolution);
            Assert.Equal(2, plan.Steps.Count);
            Assert.Contains(web, plan.Steps);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    static string CreateDotnetWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "build-graph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.sln"), "");
        WriteCsproj(Path.Combine(root, "A"), "A");
        WriteCsproj(Path.Combine(root, "B"), "B");
        return root;
    }

    static void WriteCsproj(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, name + ".csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""");
    }
}
