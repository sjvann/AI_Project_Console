using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class ReleaseWorkflowTests
{
    const string OldReleaseYaml = """
        name: GitHub Release
        on:
          push:
            tags:
              - "[0-9]*.[0-9]*.[0-9]*"
              - "hub-v[0-9]*.[0-9]*.[0-9]*"
          workflow_dispatch:
            inputs:
              tag:
                description: "既有 tag"
        jobs:
          release:
            steps:
              - uses: softprops/action-gh-release@v2
        """;

    const string NewReleaseYaml = """
        name: GitHub Release
        on:
          push:
            tags:
              - "[0-9]*.[0-9]*.[0-9]*"
              - "v[0-9]*.[0-9]*.[0-9]*"
              - "hub-v[0-9]*.[0-9]*.[0-9]*"
          workflow_dispatch:
            inputs:
              tag:
                description: "既有 tag"
        jobs:
          release:
            steps:
              - uses: softprops/action-gh-release@v2
        """;

    [Theory]
    [InlineData("[0-9]*.[0-9]*.[0-9]*", "0.4.4", true)]
    [InlineData("[0-9]*.[0-9]*.[0-9]*", "v0.4.4", false)]
    [InlineData("v[0-9]*.[0-9]*.[0-9]*", "v0.4.4", true)]
    [InlineData("v[0-9]*.[0-9]*.[0-9]*", "0.4.4", false)]
    [InlineData("hub-v[0-9]*.[0-9]*.[0-9]*", "hub-v1.2.3", true)]
    [InlineData("hub-v[0-9]*.[0-9]*.[0-9]*", "v1.2.3", false)]
    public void Glob_MatchesGitHubTagFilters(string pattern, string tag, bool expected) =>
        Assert.Equal(expected, ReleaseWorkflow.GlobMatches(pattern, tag));

    [Fact]
    public void OldWorkflow_DoesNotRunForConsoleTag()
    {
        Assert.False(ReleaseWorkflow.PushWouldRun(OldReleaseYaml, "v0.4.4"));
        Assert.True(ReleaseWorkflow.PushWouldRun(OldReleaseYaml, "0.4.3"));
    }

    [Fact]
    public void Plan_DispatchesDefaultBranchWhenTaggedCommitWouldNotPack()
    {
        var plan = ReleaseWorkflow.Plan("release.yml", OldReleaseYaml, OldReleaseYaml, "0.4.5", "main");
        Assert.NotNull(plan);
        Assert.Equal("release.yml", plan.File);
        Assert.Equal("main", plan.Ref);
        Assert.Equal("tag", plan.InputName);
        Assert.Equal("v0.4.5", plan.Tag);
        Assert.Equal(
            new[] { "workflow", "run", "release.yml", "--ref", "main", "-f", "tag=v0.4.5" },
            ReleaseWorkflow.WorkflowRunArgs(plan));
    }

    [Fact]
    public void Plan_SkipsWhenTaggedCommitAlreadyTriggers() =>
        Assert.Null(ReleaseWorkflow.Plan("release.yml", NewReleaseYaml, NewReleaseYaml, "v0.4.5", "main"));

    [Fact]
    public void Plan_UsesRemoteWorkflowWhenOnlyLocalCopyKnowsHowToDispatch() =>
        Assert.NotNull(ReleaseWorkflow.Plan("release.yml", yamlAtTag: null, NewReleaseYaml, "v0.4.5", "main"));

    [Fact]
    public void Plan_IgnoresWorkflowsThatDoNotPublishReleaseAssets()
    {
        const string pack = """
            on:
              workflow_dispatch:
                inputs:
                  tag:
                    description: version
            jobs:
              pack:
                steps:
                  - run: echo hi
            """;
        Assert.Null(ReleaseWorkflow.Plan("pack.yml", pack, pack, "v1.0.0", "main"));
    }

    [Fact]
    public void ConfirmNote_TellsUserTheDefaultBranchWillPack()
    {
        var note = ReleaseWorkflow.ConfirmNoteForYaml(OldReleaseYaml, "v0.4.4");
        Assert.Contains("預設分支", note);
        Assert.Contains("不必先把該工作流程拉進本次 tag", note);
    }
}
