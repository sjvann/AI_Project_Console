using System.Text.Json;
using System.Text.Json.Nodes;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record GithubIssue(
    int Number,
    string Title,
    string State,
    string Url,
    string Body,
    string UpdatedAt,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Assignees)
{
    public bool IsAssigned => Assignees.Count > 0;

    public string NumberText => "#" + Number;

    public string LabelText => Labels.Count == 0 ? "" : string.Join(" · ", Labels.Take(3));

    public bool AssignedTo(string login) =>
        !string.IsNullOrEmpty(login)
        && Assignees.Any(a => string.Equals(a, login, StringComparison.OrdinalIgnoreCase));

    public string WhenText()
    {
        if (string.IsNullOrWhiteSpace(UpdatedAt))
            return "";
        return DateTimeOffset.TryParse(UpdatedAt, out var dt)
            ? dt.ToLocalTime().ToString("M/d HH:mm")
            : "";
    }
}

public static class GitHubIssues
{
    public static IReadOnlyList<GithubIssue> ParseIssues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonArray arr)
                return [];
            var list = new List<GithubIssue>();
            foreach (var item in arr)
            {
                var obj = JsonUtil.Obj(item);
                if (obj is null)
                    continue;
                if (!int.TryParse(JsonUtil.Str(obj["number"]), out var number) || number <= 0)
                    continue;
                list.Add(new GithubIssue(
                    number,
                    JsonUtil.Str(obj["title"]),
                    JsonUtil.Str(obj["state"]),
                    JsonUtil.Str(obj["url"]),
                    JsonUtil.Str(obj["body"]),
                    JsonUtil.Str(obj["updatedAt"]),
                    ReadNames(obj["labels"], "name"),
                    ReadNames(obj["assignees"], "login")));
            }
            return list;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }
    }

    public static (IReadOnlyList<GithubIssue> Mine, IReadOnlyList<GithubIssue> Unassigned) Split(
        IReadOnlyList<GithubIssue> issues,
        string login)
    {
        var mine = new List<GithubIssue>();
        var open = new List<GithubIssue>();
        foreach (var issue in issues)
        {
            if (issue.AssignedTo(login))
                mine.Add(issue);
            else if (!issue.IsAssigned)
                open.Add(issue);
        }
        return (mine, open);
    }

    public static async Task<IReadOnlyList<GithubIssue>> ListOpenAsync(
        ProjectCatalog catalog,
        GithubConfig? cfg = null,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string> { "issue", "list", "--state", "open", "--limit", "50", "--json", "number,title,state,labels,url,assignees,updatedAt,body" };
        var slug = cfg.Slug();
        if (!string.IsNullOrEmpty(slug))
        {
            args.Add("--repo");
            args.Add(slug);
        }
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync("gh", args, catalog.Root, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "無法讀取 GitHub Issue。" : err);
        }
        return ParseIssues(stdout);
    }

    public static async Task AcceptAsync(
        ProjectCatalog catalog,
        int number,
        GithubConfig? cfg = null,
        CancellationToken ct = default)
    {
        if (number <= 0)
            throw new InvalidOperationException("Issue 編號無效。");
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string> { "issue", "edit", number.ToString(), "--add-assignee", "@me" };
        var slug = cfg.Slug();
        if (!string.IsNullOrEmpty(slug))
        {
            args.Add("--repo");
            args.Add(slug);
        }
        var (code, output) = await CliUtil.RunAsync("gh", args, catalog.Root, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"無法接受 Issue #{number}。" : output);
    }

    private static IReadOnlyList<string> ReadNames(JsonNode? node, string key)
    {
        var arr = JsonUtil.Arr(node);
        if (arr is null || arr.Count == 0)
            return [];
        var names = new List<string>();
        foreach (var item in arr)
        {
            var name = item is JsonValue
                ? JsonUtil.Str(item)
                : JsonUtil.Str(JsonUtil.Obj(item)?[key]);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        return names;
    }
}
