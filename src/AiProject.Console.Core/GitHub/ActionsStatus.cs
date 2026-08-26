using System.Text.Json;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 遠端 GitHub Actions 的一眼狀態。控制台只摘要，不取代 Actions 畫面或 log。
/// </summary>
public sealed record WorkflowRun(
    long DatabaseId,
    string Name,
    string DisplayTitle,
    string Status,
    string Conclusion,
    string HeadBranch,
    string Event,
    string Url,
    DateTimeOffset? UpdatedAt)
{
    public bool IsInProgress =>
        Status is "queued" or "in_progress" or "waiting" or "pending" or "requested";

    public bool IsCompleted =>
        string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);

    public bool IsSuccess =>
        IsCompleted && string.Equals(Conclusion, "success", StringComparison.OrdinalIgnoreCase);

    public bool IsFailure =>
        IsCompleted && Conclusion is "failure" or "timed_out" or "startup_failure";

    public string Tone =>
        IsInProgress ? "busy" :
        IsSuccess ? "ok" :
        IsFailure ? "warn" : "wait";

    public string ChipText()
    {
        if (IsInProgress)
            return "CI 進行中";
        if (IsSuccess)
            return "CI 通過";
        if (IsFailure)
            return "CI 失敗";
        if (string.Equals(Conclusion, "cancelled", StringComparison.OrdinalIgnoreCase))
            return "CI 取消";
        if (string.Equals(Conclusion, "skipped", StringComparison.OrdinalIgnoreCase))
            return "CI 略過";
        return string.IsNullOrEmpty(Conclusion) ? "CI " + Status : "CI " + Conclusion;
    }

    public string ResultText()
    {
        if (IsInProgress)
            return "進行中";
        if (string.IsNullOrWhiteSpace(Conclusion))
            return string.IsNullOrWhiteSpace(Status) ? "未知" : Status;
        return Conclusion;
    }

    public string Line()
    {
        var title = string.IsNullOrWhiteSpace(DisplayTitle)
            ? (string.IsNullOrWhiteSpace(Name) ? "workflow" : Name)
            : DisplayTitle;
        var branch = string.IsNullOrWhiteSpace(HeadBranch) ? "" : " · " + HeadBranch;
        return $"{title}{branch} · {ResultText()}";
    }
}

public sealed record ActionsSnapshot(
    IReadOnlyList<WorkflowRun> Runs,
    bool HasWorkflows,
    string? Error = null)
{
    public static ActionsSnapshot Unavailable(string error) => new([], false, error);

    public static ActionsSnapshot Empty(bool hasWorkflows) => new([], hasWorkflows);

    public WorkflowRun? Latest => Runs.Count > 0 ? Runs[0] : null;

    public string ChipText()
    {
        if (!string.IsNullOrEmpty(Error))
            return "CI 無法讀取";
        if (!HasWorkflows && Runs.Count == 0)
            return "CI 無";
        return Latest?.ChipText() ?? "CI 尚無執行";
    }

    public string ChipTone()
    {
        if (!string.IsNullOrEmpty(Error))
            return "wait";
        return Latest?.Tone ?? "wait";
    }

    public string FormatReport()
    {
        if (!string.IsNullOrEmpty(Error))
            return Error;
        if (Runs.Count == 0)
            return HasWorkflows ? "（尚無 workflow 執行）" : "（這個倉庫還沒有 GitHub Actions）";
        var lines = new List<string> { "最近 Actions：", "" };
        foreach (var run in Runs)
            lines.Add("  " + run.Line());
        return string.Join('\n', lines);
    }
}

public static class ActionsStatus
{
    public static IReadOnlyList<WorkflowRun> ParseRuns(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<WorkflowRun>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            list.Add(new WorkflowRun(
                DatabaseId: ReadInt64(el, "databaseId"),
                Name: ReadStr(el, "name", "workflowName"),
                DisplayTitle: ReadStr(el, "displayTitle"),
                Status: ReadStr(el, "status"),
                Conclusion: ReadStr(el, "conclusion"),
                HeadBranch: ReadStr(el, "headBranch"),
                Event: ReadStr(el, "event"),
                Url: ReadStr(el, "url"),
                UpdatedAt: ReadTime(el, "updatedAt", "createdAt")));
        }
        return list;
    }

    public static WorkflowRun? PreferWatched(ActionsSnapshot snap, string? branch, DateTimeOffset startedUtc)
    {
        if (snap.Runs.Count == 0)
            return null;
        var floor = startedUtc.AddSeconds(-45);
        var onBranch = snap.Runs.Where(r =>
            string.IsNullOrEmpty(branch)
            || string.Equals(r.HeadBranch, branch, StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = onBranch.Count > 0 ? onBranch : snap.Runs;
        return pool.FirstOrDefault(r => r.IsInProgress || r.UpdatedAt is null || r.UpdatedAt >= floor)
            ?? pool[0];
    }

    static string ReadStr(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            var s = p.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        }
        return "";
    }

    static long ReadInt64(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
            return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n))
            return n;
        return long.TryParse(p.GetString(), out var parsed) ? parsed : 0;
    }

    static DateTimeOffset? ReadTime(JsonElement el, params string[] names)
    {
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                continue;
            var raw = p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
            if (DateTimeOffset.TryParse(raw, out var t))
                return t;
        }
        return null;
    }
}
