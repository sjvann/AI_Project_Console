using System.Text.Json;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 目前分支上的 PR 與檢查摘要。控制台只回答能不能請人審，不取代 GitHub 審查畫面。
/// </summary>
public sealed record PrCheck(string Name, string Status, string Conclusion, string Url)
{
    public bool IsPending =>
        IsAny(Status, "queued", "in_progress", "pending", "expected", "requested")
        || IsAny(Conclusion, "pending");

    public bool IsSuccess =>
        IsAny(Conclusion, "success", "neutral", "skipped")
        || IsAny(Status, "success");

    public bool IsFailure =>
        IsAny(Conclusion, "failure", "timed_out", "startup_failure", "action_required", "error")
        || IsAny(Status, "failure", "error");

    public string ResultText()
    {
        if (IsPending)
            return "進行中";
        if (IsSuccess)
            return "通過";
        if (IsFailure)
            return "失敗";
        return string.IsNullOrWhiteSpace(Conclusion) ? Status : Conclusion;
    }

    public string Line() =>
        (string.IsNullOrWhiteSpace(Name) ? "check" : Name) + " · " + ResultText();

    static bool IsAny(string value, params string[] options) =>
        options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));
}

public sealed record PullRequestStatus(
    bool HasPr,
    string Title,
    string Url,
    string State,
    bool IsDraft,
    string Head,
    string Base,
    string ReviewDecision,
    IReadOnlyList<PrCheck> Checks,
    string? Error = null)
{
    public static PullRequestStatus None() =>
        new(false, "", "", "", false, "", "", "", []);

    public static PullRequestStatus Unavailable(string error) =>
        new(false, "", "", "", false, "", "", "", [], error);

    public int FailedCount => Checks.Count(c => c.IsFailure);

    public int PendingCount => Checks.Count(c => c.IsPending);

    public int PassedCount => Checks.Count(c => c.IsSuccess);

    public bool ReadyForReview =>
        HasPr && !IsDraft && Checks.Count > 0 && FailedCount == 0 && PendingCount == 0;

    public string ChipText()
    {
        if (!string.IsNullOrEmpty(Error))
            return "PR 無法讀取";
        if (!HasPr)
            return "無 PR";
        if (IsDraft)
            return "PR 草稿";
        if (FailedCount > 0)
            return "PR 檢查未過";
        if (PendingCount > 0)
            return "PR 檢查中";
        if (Checks.Count == 0)
            return "PR 尚無檢查";
        return "PR 可請人審";
    }

    public string ChipTone()
    {
        if (!string.IsNullOrEmpty(Error) || !HasPr)
            return "wait";
        if (FailedCount > 0)
            return "warn";
        if (PendingCount > 0)
            return "busy";
        if (ReadyForReview)
            return "ok";
        return "wait";
    }

    public string Hint()
    {
        if (!string.IsNullOrEmpty(Error))
            return Error;
        if (!HasPr)
            return "目前分支還沒有 PR。可用「建立／開啟 PR」自動填說明建立；審查與合併請到 GitHub。";
        var checks = Checks.Count == 0
            ? "尚無檢查。可先補齊 CI workflow。"
            : $"檢查 {PassedCount} 通過、{FailedCount} 失敗、{PendingCount} 進行中。";
        var draft = IsDraft ? "這是草稿。" : ReadyForReview ? "檢查已過，可以請人審。" : "";
        return string.Join(" ", new[] { checks, draft }.Where(s => s.Length > 0));
    }
}

public static class PrStatus
{
    public static bool LooksLikeNoPr(string? output)
    {
        var text = output ?? "";
        return text.Contains("no pull requests found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("no open pull requests", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Could not find a pull request", StringComparison.OrdinalIgnoreCase);
    }

    public static PullRequestStatus Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var el = doc.RootElement;
        var url = Read(el, "url");
        var title = Read(el, "title");
        var hasPr = !string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(title);
        return new PullRequestStatus(
            HasPr: hasPr,
            Title: title,
            Url: url,
            State: Read(el, "state"),
            IsDraft: el.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
            Head: Read(el, "headRefName"),
            Base: Read(el, "baseRefName"),
            ReviewDecision: Read(el, "reviewDecision"),
            Checks: ParseChecks(el));
    }

    public static IReadOnlyList<PrCheck> ParseChecks(JsonElement parent)
    {
        if (!parent.TryGetProperty("statusCheckRollup", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<PrCheck>();
        foreach (var el in arr.EnumerateArray())
        {
            var name = FirstNonEmpty(Read(el, "name"), Read(el, "context"), "check");
            var status = FirstNonEmpty(Read(el, "status"), Read(el, "state"));
            var conclusion = FirstNonEmpty(Read(el, "conclusion"), Read(el, "state"));
            var url = FirstNonEmpty(Read(el, "detailsUrl"), Read(el, "targetUrl"));
            list.Add(new PrCheck(name, status, conclusion, url));
        }
        return list;
    }

    static string Read(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return "";
        return (p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString())?.Trim() ?? "";
    }

    static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
}
