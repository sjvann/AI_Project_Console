using System.Text.Json.Nodes;
using AiProject.Console.Core.Intake;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public static class GitHubLifecycle
{
    public static async Task<IReadOnlyList<IssueTrace>> LoadTracesAsync(
        string cwd,
        GithubConfig cfg,
        IEnumerable<int> issueNumbers,
        CancellationToken ct = default)
    {
        var numbers = issueNumbers.Where(n => n > 0).Distinct().ToList();
        if (numbers.Count == 0)
            return [];
        var issues = await GitHubIssues.ListAsync(cwd, cfg, "all", 100, ct).ConfigureAwait(false);
        var pulls = await ListPullsAsync(cwd, cfg, ct).ConfigureAwait(false);
        var list = new List<IssueTrace>();
        foreach (var number in numbers)
        {
            var issue = issues.FirstOrDefault(i => i.Number == number);
            var pr = pulls.FirstOrDefault(p => p.Closes.Contains(number) || p.Title.Contains("#" + number, StringComparison.Ordinal));
            list.Add(new IssueTrace(
                number,
                issue?.Title ?? "",
                issue?.State ?? "",
                issue?.Url ?? "",
                issue?.Assignees ?? [],
                pr?.Url ?? "",
                pr?.State ?? "",
                pr?.CiTone ?? "",
                pr?.CiHint ?? "",
                pr?.ChecksGreen ?? false,
                pr?.Merged ?? false));
        }
        return list;
    }

    public static async Task<IReadOnlyList<LinkedPull>> ListPullsAsync(
        string cwd,
        GithubConfig cfg,
        CancellationToken ct = default)
    {
        if (!GitHubService.GhAvailable())
            return [];
        var args = new List<string>
        {
            "pr", "list", "--state", "all", "--limit", "40",
            "--json", "number,title,url,state,body,statusCheckRollup,mergedAt",
        };
        GhCli.AddRepo(args, cfg);
        var (code, stdout, _) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            return [];
        return ParsePulls(stdout);
    }

    public static IReadOnlyList<LinkedPull> ParsePulls(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is not JsonArray arr)
                return [];
            var list = new List<LinkedPull>();
            foreach (var item in arr)
            {
                var obj = JsonUtil.Obj(item);
                if (obj is null || !int.TryParse(JsonUtil.Str(obj["number"]), out var number) || number <= 0)
                    continue;
                var body = JsonUtil.Str(obj["body"]);
                var title = JsonUtil.Str(obj["title"]);
                var checks = obj["statusCheckRollup"] as JsonArray;
                var failed = 0;
                var pending = 0;
                var passed = 0;
                if (checks is not null)
                {
                    foreach (var check in checks)
                    {
                        var c = JsonUtil.Obj(check);
                        if (c is null)
                            continue;
                        var conclusion = JsonUtil.Pick(JsonUtil.Str(c["conclusion"]), JsonUtil.Str(c["state"]));
                        if (conclusion.Contains("fail", StringComparison.OrdinalIgnoreCase)
                            || conclusion.Contains("error", StringComparison.OrdinalIgnoreCase))
                            failed++;
                        else if (conclusion.Contains("pend", StringComparison.OrdinalIgnoreCase)
                                 || conclusion.Contains("progress", StringComparison.OrdinalIgnoreCase)
                                 || conclusion.Contains("queued", StringComparison.OrdinalIgnoreCase))
                            pending++;
                        else
                            passed++;
                    }
                }
                var tone = failed > 0 ? "warn" : pending > 0 ? "wait" : passed > 0 ? "ok" : "";
                var hint = failed > 0 ? "CI 未通過" : pending > 0 ? "CI 進行中" : passed > 0 ? "檢查已過" : "尚無檢查";
                list.Add(new LinkedPull(
                    number,
                    title,
                    JsonUtil.Str(obj["url"]),
                    JsonUtil.Str(obj["state"]),
                    FindIssueNumbers(title + "\n" + body),
                    tone,
                    hint,
                    failed == 0 && pending == 0 && passed > 0,
                    !string.IsNullOrEmpty(JsonUtil.Str(obj["mergedAt"]))
                        || string.Equals(JsonUtil.Str(obj["state"]), "MERGED", StringComparison.OrdinalIgnoreCase)));
            }
            return list;
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<int> FindIssueNumbers(string text)
    {
        var list = new List<int>();
        var raw = text ?? "";
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '#')
                continue;
            var j = i + 1;
            while (j < raw.Length && char.IsDigit(raw[j]))
                j++;
            if (j > i + 1 && int.TryParse(raw[(i + 1)..j], out var n) && n > 0 && !list.Contains(n))
                list.Add(n);
        }
        return list;
    }

    public static async Task RequestReviewAsync(
        string cwd,
        GithubConfig cfg,
        int prNumber,
        string reviewer,
        CancellationToken ct = default)
    {
        if (prNumber <= 0 || string.IsNullOrWhiteSpace(reviewer))
            throw new InvalidOperationException("請指定 PR 與審查人。");
        var args = new List<string> { "pr", "edit", prNumber.ToString(), "--add-reviewer", reviewer.Trim().TrimStart('@') };
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "無法請人審查。" : output);
    }

    public static async Task MergeAsync(
        string cwd,
        GithubConfig cfg,
        int prNumber,
        bool allowWhenRed,
        CancellationToken ct = default)
    {
        if (prNumber <= 0)
            throw new InvalidOperationException("PR 編號無效。");
        var args = new List<string> { "pr", "merge", prNumber.ToString(), "--merge" };
        _ = allowWhenRed;
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 120_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"無法合併 PR #{prNumber}。" : output);
    }

    public static async Task<string> DispatchWorkflowAsync(
        string cwd,
        GithubConfig cfg,
        string workflow,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workflow))
            throw new InvalidOperationException("尚未設定部署 workflow。");
        var args = new List<string> { "workflow", "run", workflow.Trim() };
        GhCli.AddRepo(args, cfg);
        var (code, output) = await GhCli.RunAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "無法觸發 workflow。" : output);
        return string.IsNullOrEmpty(output) ? "已觸發 " + workflow : output;
    }

    public static async Task<WorkflowRun?> LatestRunAsync(
        string cwd,
        GithubConfig cfg,
        string workflow,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workflow))
            return null;
        var args = new List<string>
        {
            "run", "list", "--workflow", workflow.Trim(), "--limit", "1",
            "--json", "databaseId,status,conclusion,url,displayTitle",
        };
        GhCli.AddRepo(args, cfg);
        var (code, stdout, _) = await GhCli.RunCaptureAsync(args, cwd, cfg, 60_000, ct).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;
        return ActionsStatus.ParseRuns(stdout).FirstOrDefault();
    }
}

public sealed record LinkedPull(
    int Number,
    string Title,
    string Url,
    string State,
    IReadOnlyList<int> Closes,
    string CiTone,
    string CiHint,
    bool ChecksGreen,
    bool Merged);
