using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.WorkHours;

public sealed record GithubPull(
    int Number,
    string Title,
    string State,
    string Url,
    string CreatedAt,
    string MergedAt);

public static class WorkHoursContributions
{
    public static bool InRange(DateTimeOffset? at, DateOnly start, DateOnly end)
    {
        if (at is null)
            return false;
        var date = DateOnly.FromDateTime(at.Value.ToLocalTime().DateTime);
        return date >= start && date <= end;
    }

    public static DateTimeOffset? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }

    public static IReadOnlyList<TimesheetItem> FromClosedIssues(
        IEnumerable<GithubIssue> issues,
        DateOnly start,
        DateOnly end,
        string projectKey,
        string projectName,
        string githubSlug)
    {
        var list = new List<TimesheetItem>();
        foreach (var issue in issues)
        {
            var at = ParseTime(issue.ClosedAt) ?? ParseTime(issue.UpdatedAt);
            if (!InRange(at, start, end))
                continue;
            list.Add(new TimesheetItem(
                "issue",
                issue.Number,
                issue.Title,
                issue.Url,
                string.IsNullOrWhiteSpace(issue.State) ? "closed" : issue.State,
                at,
                projectKey,
                projectName,
                githubSlug));
        }
        return list
            .OrderBy(i => i.At)
            .ThenBy(i => i.Number)
            .ToList();
    }

    public static IReadOnlyList<GithubPull> ParsePulls(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonArray arr)
                return [];
            var list = new List<GithubPull>();
            foreach (var item in arr)
            {
                var obj = JsonUtil.Obj(item);
                if (obj is null)
                    continue;
                if (!int.TryParse(JsonUtil.Str(obj["number"]), out var number) || number <= 0)
                    continue;
                list.Add(new GithubPull(
                    number,
                    JsonUtil.Str(obj["title"]),
                    JsonUtil.Str(obj["state"]),
                    JsonUtil.Str(obj["url"]),
                    JsonUtil.Pick(JsonUtil.Str(obj["createdAt"]), JsonUtil.Str(obj["created_at"])),
                    JsonUtil.Pick(JsonUtil.Str(obj["mergedAt"]), JsonUtil.Str(obj["merged_at"]))));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    public static IReadOnlyList<TimesheetItem> FromPulls(
        IEnumerable<GithubPull> pulls,
        DateOnly start,
        DateOnly end,
        string projectKey,
        string projectName,
        string githubSlug)
    {
        var list = new List<TimesheetItem>();
        foreach (var pull in pulls)
        {
            var created = ParseTime(pull.CreatedAt);
            var merged = ParseTime(pull.MergedAt);
            DateTimeOffset? at = null;
            if (InRange(merged, start, end))
                at = merged;
            else if (InRange(created, start, end))
                at = created;
            if (at is null)
                continue;
            list.Add(new TimesheetItem(
                "pr",
                pull.Number,
                pull.Title,
                pull.Url,
                string.IsNullOrWhiteSpace(pull.State) ? "open" : pull.State,
                at,
                projectKey,
                projectName,
                githubSlug));
        }
        return list
            .OrderBy(i => i.At)
            .ThenBy(i => i.Number)
            .ToList();
    }

    public static async Task<IReadOnlyList<TimesheetItem>> LoadAsync(
        string projectKey,
        string projectName,
        string githubSlug,
        DateOnly start,
        DateOnly end,
        string? cwd = null,
        CancellationToken ct = default,
        string? host = null)
    {
        if (string.IsNullOrWhiteSpace(githubSlug))
            return [];
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var root = string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd;
        var cfg = new GithubConfig { Host = GitHost.Normalize(host) };
        var issues = FromClosedIssues(
            await ListClosedAssignedAsync(githubSlug, root, cfg, ct).ConfigureAwait(false),
            start,
            end,
            projectKey,
            projectName,
            githubSlug);
        var pulls = FromPulls(
            await ListAuthoredPullsAsync(githubSlug, root, cfg, ct).ConfigureAwait(false),
            start,
            end,
            projectKey,
            projectName,
            githubSlug);
        return issues.Concat(pulls).OrderBy(i => i.At).ThenBy(i => i.Kind).ThenBy(i => i.Number).ToList();
    }

    static async Task<IReadOnlyList<GithubIssue>> ListClosedAssignedAsync(
        string slug,
        string cwd,
        GithubConfig cfg,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "issue", "list",
            "--state", "closed",
            "--assignee", "@me",
            "--limit", "100",
            "--json", "number,title,state,url,closedAt,updatedAt,assignees",
            "--repo", slug,
        };
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "無法讀取已關閉的 Issue。" : err);
        }
        return GitHubIssues.ParseIssues(stdout);
    }

    static async Task<IReadOnlyList<GithubPull>> ListAuthoredPullsAsync(
        string slug,
        string cwd,
        GithubConfig cfg,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "pr", "list",
            "--author", "@me",
            "--state", "all",
            "--limit", "100",
            "--json", "number,title,url,state,createdAt,mergedAt",
            "--repo", slug,
        };
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "無法讀取 Pull Request。" : err);
        }
        return ParsePulls(stdout);
    }
}
