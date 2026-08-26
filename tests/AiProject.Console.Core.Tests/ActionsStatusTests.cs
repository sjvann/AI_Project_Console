using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class ActionsStatusTests
{
    const string Sample = """
        [
          {
            "databaseId": 101,
            "name": "CI",
            "displayTitle": "CI",
            "status": "completed",
            "conclusion": "success",
            "headBranch": "main",
            "event": "push",
            "url": "https://github.com/acme/app/actions/runs/101",
            "updatedAt": "2026-08-26T01:00:00Z"
          },
          {
            "databaseId": 99,
            "name": "Deploy docs",
            "displayTitle": "docs",
            "status": "in_progress",
            "conclusion": "",
            "headBranch": "feat",
            "event": "pull_request",
            "url": "https://github.com/acme/app/actions/runs/99",
            "updatedAt": "2026-08-26T01:05:00Z"
          }
        ]
        """;

    [Fact]
    public void ParseRuns_ReadsJsonFields()
    {
        var runs = ActionsStatus.ParseRuns(Sample);
        Assert.Equal(2, runs.Count);
        Assert.Equal(101, runs[0].DatabaseId);
        Assert.True(runs[0].IsSuccess);
        Assert.Equal("CI 通過", runs[0].ChipText());
        Assert.Equal("ok", runs[0].Tone);
        Assert.True(runs[1].IsInProgress);
        Assert.Equal("CI 進行中", runs[1].ChipText());
        Assert.Equal("busy", runs[1].Tone);
        Assert.Contains("feat", runs[1].Line());
    }

    [Fact]
    public void Snapshot_Chip_WhenEmptyOrFailed()
    {
        Assert.Equal("CI 無", ActionsSnapshot.Empty(false).ChipText());
        Assert.Equal("CI 尚無執行", ActionsSnapshot.Empty(true).ChipText());
        Assert.Equal("CI 無法讀取", ActionsSnapshot.Unavailable("需要 gh").ChipText());
        var snap = new ActionsSnapshot(ActionsStatus.ParseRuns(Sample), true);
        Assert.Equal("CI 通過", snap.ChipText());
        Assert.Contains("最近 Actions", snap.FormatReport());
    }

    [Fact]
    public void PreferWatched_PicksInProgressOnBranch()
    {
        var snap = new ActionsSnapshot(ActionsStatus.ParseRuns(Sample), true);
        var started = DateTimeOffset.Parse("2026-08-26T01:04:00Z");
        var watched = ActionsStatus.PreferWatched(snap, "feat", started);
        Assert.NotNull(watched);
        Assert.Equal("feat", watched!.HeadBranch);
        Assert.True(watched.IsInProgress);
    }

    [Fact]
    public void ConsoleChipTone_MutesHistoricalFailure()
    {
        Assert.Equal("wait", ActionsSnapshot.ConsoleChipTone("warn", watchingAfterPush: false));
        Assert.Equal("warn", ActionsSnapshot.ConsoleChipTone("warn", watchingAfterPush: true));
        Assert.Equal("ok", ActionsSnapshot.ConsoleChipTone("ok", watchingAfterPush: false));
        Assert.Equal("busy", ActionsSnapshot.ConsoleChipTone("busy", watchingAfterPush: true));
    }

    [Fact]
    public void ParseRuns_EmptyOrInvalid_ReturnsEmpty()
    {
        Assert.Empty(ActionsStatus.ParseRuns(""));
        Assert.Empty(ActionsStatus.ParseRuns("[]"));
    }
}
