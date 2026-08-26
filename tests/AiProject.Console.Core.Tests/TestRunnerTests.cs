using System.Text.Json.Nodes;
using AiProject.Console.Core;
using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.Tests;

public class TestRunnerTests
{
    [Fact]
    public void FindSolution_PrefersSlnxMatchingFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "AiHouse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Other.slnx"), "");
            File.WriteAllText(Path.Combine(root, Path.GetFileName(root) + ".slnx"), "");
            var found = TestRunner.FindSolution(root);
            Assert.Equal(Path.GetFileName(root) + ".slnx", Path.GetFileName(found));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void TargetsFor_UsesSolutionBeforeTestProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-test-tgt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "tests", "Demo.Tests"));
        try
        {
            var sln = Path.Combine(root, "Demo.sln");
            File.WriteAllText(sln, "");
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "Demo",
                Services = [],
                Projects =
                [
                    new ProjectInfo("tests/Demo.Tests", "Demo.Tests", Path.Combine(root, "tests", "Demo.Tests", "Demo.Tests.csproj"),
                        "Microsoft.NET.Sdk", "Library", false, false, false, true, [], [], "", "tests"),
                ],
                StartOrder = [],
                Frontend = "",
                Manifest = new JsonObject(),
                Scan = new ScanResult(root, []),
            };
            var targets = TestRunner.TargetsFor(catalog);
            Assert.Single(targets);
            Assert.Equal(sln, targets[0]);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp */ }
        }
    }
}
