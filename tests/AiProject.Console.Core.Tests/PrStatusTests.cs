using AiProject.Console.Core.GitHub;

namespace AiProject.Console.Core.Tests;

public class PrStatusTests
{
    const string Sample = """
        {
          "title": "Add CI",
          "url": "https://github.com/acme/app/pull/12",
          "state": "OPEN",
          "isDraft": false,
          "headRefName": "feat/ci",
          "baseRefName": "main",
          "reviewDecision": "REVIEW_REQUIRED",
          "statusCheckRollup": [
            {
              "__typename": "CheckRun",
              "name": "CI / test",
              "status": "COMPLETED",
              "conclusion": "SUCCESS",
              "detailsUrl": "https://github.com/acme/app/actions/runs/1"
            },
            {
              "__typename": "StatusContext",
              "context": "deploy",
              "state": "PENDING",
              "targetUrl": "https://github.com/acme/app/actions/runs/2"
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ReadsTitleChecksAndChip()
    {
        var pr = PrStatus.Parse(Sample);
        Assert.True(pr.HasPr);
        Assert.Equal("Add CI", pr.Title);
        Assert.Equal("feat/ci", pr.Head);
        Assert.Equal(2, pr.Checks.Count);
        Assert.True(pr.Checks[0].IsSuccess);
        Assert.True(pr.Checks[1].IsPending);
        Assert.Equal("PR 檢查中", pr.ChipText());
        Assert.Equal("busy", pr.ChipTone());
        Assert.False(pr.ReadyForReview);
        Assert.Contains("CI / test", pr.Checks[0].Line());
    }

    [Fact]
    public void ReadyForReview_WhenAllChecksPass()
    {
        var json = Sample.Replace("\"PENDING\"", "\"SUCCESS\"");
        var pr = PrStatus.Parse(json);
        Assert.True(pr.ReadyForReview);
        Assert.Equal("PR 可請人審", pr.ChipText());
        Assert.Equal("ok", pr.ChipTone());
    }

    [Fact]
    public void FailedCheck_Warns()
    {
        var json = """
            {
              "title": "Fix",
              "url": "https://github.com/acme/app/pull/3",
              "state": "OPEN",
              "isDraft": false,
              "headRefName": "fix",
              "baseRefName": "main",
              "statusCheckRollup": [
                { "name": "CI", "status": "COMPLETED", "conclusion": "FAILURE", "detailsUrl": "https://example" }
              ]
            }
            """;
        var pr = PrStatus.Parse(json);
        Assert.Equal("PR 檢查未過", pr.ChipText());
        Assert.Equal("warn", pr.ChipTone());
        Assert.Contains("失敗", pr.Hint());
    }

    [Fact]
    public void NoneAndUnavailable()
    {
        Assert.Equal("無 PR", PullRequestStatus.None().ChipText());
        Assert.Equal("PR 無法讀取", PullRequestStatus.Unavailable("需要 gh").ChipText());
        Assert.True(PrStatus.LooksLikeNoPr("no pull requests found for branch \"feat\""));
        Assert.False(PrStatus.LooksLikeNoPr("HTTP 403"));
    }
}
