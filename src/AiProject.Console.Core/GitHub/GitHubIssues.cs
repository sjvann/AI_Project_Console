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
    IReadOnlyList<string> Assignees,
    string ClosedAt = "",
    string CiTone = "",
    string CiHint = "",
    string PrUrl = "",
    string PrState = "")
{
    public bool IsAssigned => Assignees.Count > 0;

    public string NumberText => "#" + Number;

    public string LabelText => Labels.Count == 0 ? "" : string.Join(" · ", Labels.Take(3));

    public string AssigneeText => Assignees.Count == 0 ? "未指派" : string.Join(" · ", Assignees);

    public string TraceChipTone => string.IsNullOrEmpty(CiTone) ? (string.IsNullOrEmpty(PrUrl) ? "" : "wait") : CiTone;

    public string TraceChipText =>
        !string.IsNullOrEmpty(CiHint) ? CiHint
        : !string.IsNullOrEmpty(PrState) ? "PR " + PrState
        : "";

    public bool AssignedTo(string login) =>
        !string.IsNullOrEmpty(login)
        && Assignees.Any(a => string.Equals(a, login, StringComparison.OrdinalIgnoreCase));

    public string WhenText() => FormatWhen(UpdatedAt);

    public static string FormatWhen(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "";
        return DateTimeOffset.TryParse(iso, out var dt)
            ? dt.ToLocalTime().ToString("M/d HH:mm")
            : "";
    }
}

public sealed record GithubIssueComment(
    string Author,
    string Body,
    string CreatedAt,
    string Url = "")
{
    public string AuthorText => string.IsNullOrEmpty(Author) ? "未知" : Author;

    public string WhenText() => GithubIssue.FormatWhen(CreatedAt);
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
                var issue = ReadIssue(JsonUtil.Obj(item));
                if (issue is not null)
                    list.Add(issue);
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
        return await ListAsync(catalog.Root, cfg, "open", 50, ct).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<GithubIssue>> ListAsync(
        string cwd,
        GithubConfig cfg,
        string state = "open",
        int limit = 100,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var args = new List<string>
        {
            "issue", "list", "--state", state, "--limit", limit.ToString(),
            "--json", "number,title,state,labels,url,assignees,updatedAt,body,closedAt",
        };
        GhCli.AddRepo(args, cfg);
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "無法讀取遠端 Issue。" : err);
        }
        return ParseIssues(stdout);
    }

    public static (GithubIssue? Issue, IReadOnlyList<GithubIssueComment> Comments) ParseView(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (null, []);
        try
        {
            var obj = JsonUtil.Obj(JsonNode.Parse(json));
            if (obj is null)
                return (null, []);
            return (ReadIssue(obj), ReadComments(JsonUtil.Arr(obj["comments"])));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, []);
        }
    }

    public static async Task<(GithubIssue Issue, IReadOnlyList<GithubIssueComment> Comments)> ViewAsync(
        string cwd,
        GithubConfig cfg,
        int number,
        CancellationToken ct = default)
    {
        if (number <= 0)
            throw new InvalidOperationException("Issue 編號無效。");
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var args = new List<string>
        {
            "issue", "view", number.ToString(),
            "--json", "number,title,state,labels,url,assignees,updatedAt,body,closedAt,comments",
        };
        GhCli.AddRepo(args, cfg);
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? $"無法讀取 Issue #{number}。" : err);
        }
        var (issue, comments) = ParseView(stdout);
        if (issue is null)
            throw new InvalidOperationException($"無法解析 Issue #{number}。");
        return (issue, comments);
    }

    public static async Task AcceptAsync(
        ProjectCatalog catalog,
        int number,
        GithubConfig? cfg = null,
        CancellationToken ct = default)
    {
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        await AssignAsync(catalog.Root, cfg, number, "@me", ct).ConfigureAwait(false);
    }

    public static async Task AssignAsync(
        string cwd,
        GithubConfig cfg,
        int number,
        string login,
        CancellationToken ct = default)
    {
        if (number <= 0)
            throw new InvalidOperationException("Issue 編號無效。");
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var who = string.IsNullOrWhiteSpace(login) ? "@me" : login.Trim().TrimStart('@');
        if (who == "me")
            who = "@me";
        var args = new List<string> { "issue", "edit", number.ToString(), "--add-assignee", who };
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"無法指派 Issue #{number}。" : output);
    }

    public static async Task<(int Number, string Url)> CreateAsync(
        string cwd,
        GithubConfig cfg,
        string title,
        string body,
        IEnumerable<string>? labels = null,
        string? assignee = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Issue 標題不能空白。");
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var args = new List<string> { "issue", "create", "--title", title.Trim(), "--body", body ?? "" };
        foreach (var label in (labels ?? []).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            args.Add("--label");
            args.Add(label.Trim());
        }
        if (!string.IsNullOrWhiteSpace(assignee))
        {
            args.Add("--assignee");
            args.Add(assignee.Trim().TrimStart('@'));
        }
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "無法建立 Issue。" : output);
        return ParseCreated(output, cfg);
    }

    public static async Task CommentAsync(
        string cwd,
        GithubConfig cfg,
        int number,
        string body,
        CancellationToken ct = default)
    {
        if (number <= 0)
            throw new InvalidOperationException("Issue 編號無效。");
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("留言不能空白。");
        if (!GitHubService.GhAvailable())
            throw new InvalidOperationException("尚未安裝 GitHub CLI（gh）。");
        var args = new List<string> { "issue", "comment", number.ToString(), "--body", body.Trim() };
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"無法在 Issue #{number} 留言。" : output);
    }

    public static async Task CloseAsync(
        string cwd,
        GithubConfig cfg,
        int number,
        CancellationToken ct = default,
        string? reason = null)
    {
        if (number <= 0)
            throw new InvalidOperationException("Issue 編號無效。");
        var args = new List<string> { "issue", "close", number.ToString() };
        if (!string.IsNullOrWhiteSpace(reason))
        {
            args.Add("--reason");
            args.Add(reason.Trim());
        }
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0 && !string.IsNullOrWhiteSpace(reason))
        {
            args = ["issue", "close", number.ToString()];
            GhCli.AddRepo(args, cfg);
            (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        }
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"無法關閉 Issue #{number}。" : output);
    }

    public static async Task<IReadOnlyList<string>> ListCollaboratorsAsync(
        string cwd,
        GithubConfig cfg,
        CancellationToken ct = default)
    {
        var slug = cfg.Slug();
        if (string.IsNullOrEmpty(slug))
            return [];
        var args = new List<string> { "api", $"repos/{slug}/collaborators", "--jq", ".[].login" };
        var (code, stdout, _) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];
        return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (int Number, string Url) ParseCreated(string output, GithubConfig? cfg = null)
    {
        var text = (output ?? "").Trim();
        var url = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Contains("/issues/", StringComparison.OrdinalIgnoreCase))
            ?? text;
        var hash = url.LastIndexOf('/');
        if (hash >= 0 && int.TryParse(url[(hash + 1)..].Trim(), out var number) && number > 0)
            return (number, url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : (cfg?.WebUrl() + "/issues/" + number));
        throw new InvalidOperationException("已建立 Issue，但無法解析編號：\n" + text);
    }

    private static GithubIssue? ReadIssue(JsonObject? obj)
    {
        if (obj is null)
            return null;
        if (!int.TryParse(JsonUtil.Str(obj["number"]), out var number) || number <= 0)
            return null;
        return new GithubIssue(
            number,
            JsonUtil.Str(obj["title"]),
            JsonUtil.Str(obj["state"]),
            JsonUtil.Str(obj["url"]),
            JsonUtil.Str(obj["body"]),
            JsonUtil.Str(obj["updatedAt"]),
            ReadNames(obj["labels"], "name"),
            ReadNames(obj["assignees"], "login"),
            JsonUtil.Pick(JsonUtil.Str(obj["closedAt"]), JsonUtil.Str(obj["closed_at"])));
    }

    private static IReadOnlyList<GithubIssueComment> ReadComments(JsonArray? arr)
    {
        if (arr is null || arr.Count == 0)
            return [];
        var list = new List<GithubIssueComment>();
        foreach (var item in arr)
        {
            var obj = JsonUtil.Obj(item);
            if (obj is null)
                continue;
            var body = JsonUtil.Str(obj["body"]);
            var author = ReadAuthor(obj["author"]);
            var created = JsonUtil.Pick(JsonUtil.Str(obj["createdAt"]), JsonUtil.Str(obj["created_at"]));
            if (string.IsNullOrEmpty(body) && string.IsNullOrEmpty(author))
                continue;
            list.Add(new GithubIssueComment(
                author,
                body,
                created,
                JsonUtil.Str(obj["url"])));
        }
        return list;
    }

    private static string ReadAuthor(JsonNode? node)
    {
        if (node is null)
            return "";
        if (node is JsonValue)
            return JsonUtil.Str(node);
        var obj = JsonUtil.Obj(node);
        return JsonUtil.Pick(JsonUtil.Str(obj?["login"]), JsonUtil.Str(obj?["name"]));
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
