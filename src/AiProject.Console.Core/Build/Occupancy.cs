namespace AiProject.Console.Core.Build;

public static class OccupancySources
{
    public const string Desktop = "desktop";
    public const string Mcp = "mcp";
}

public enum OccupancyKind
{
    Other,
    Build,
    Test,
    Scan,
    Git,
    Start,
    Stop,
    Update,
    Pack,
    McpBuild,
}

/// <summary>
/// 控制台長工作的可見佔用（AD-32）。不是全域布林。
/// </summary>
public sealed record OccupancySnapshot(
    bool IsActive,
    OccupancyKind Kind,
    string Title,
    string Source,
    int Pid,
    DateTimeOffset StartedUtc,
    int Done,
    int Total,
    string? CurrentName,
    string? QueuedTitle)
{
    public static OccupancySnapshot Idle { get; } = new(
        false,
        OccupancyKind.Other,
        "",
        OccupancySources.Desktop,
        0,
        default,
        0,
        0,
        null,
        null);

    public bool CanCancel => IsActive && Source == OccupancySources.Desktop;

    public string Headline
    {
        get
        {
            if (!IsActive)
                return "";
            if (Source == OccupancySources.Mcp || Kind == OccupancyKind.McpBuild)
                return "Agent 正在編譯";
            return StripEllipsis(Title);
        }
    }

    public string ProgressText =>
        !IsActive || Total <= 0 ? "" : $"{Done}/{Total}";

    public string ElapsedText
    {
        get
        {
            if (!IsActive || StartedUtc == default)
                return "";
            var elapsed = DateTimeOffset.UtcNow - StartedUtc;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            var totalSeconds = (int)elapsed.TotalSeconds;
            var m = totalSeconds / 60;
            var s = totalSeconds % 60;
            return m > 0 ? $"{m}:{s:D2}" : $"0:{s:D2}";
        }
    }

    public string BarTitle
    {
        get
        {
            if (!IsActive)
                return "";
            var progress = string.IsNullOrEmpty(ProgressText) ? "" : " " + ProgressText;
            var current = string.IsNullOrEmpty(CurrentName) ? "" : " · " + CurrentName;
            return Headline + progress + current;
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (!IsActive)
                return "";
            var elapsed = string.IsNullOrEmpty(ElapsedText) ? "" : " · " + ElapsedText;
            var queued = string.IsNullOrEmpty(QueuedTitle) ? "" : "（下一個：" + StripEllipsis(QueuedTitle) + "）";
            return BarTitle + elapsed + queued;
        }
    }

    public string BusyReason => IsActive ? "忙碌中：" + BarTitle : "";

    public string BusyDialogBody
    {
        get
        {
            if (!IsActive)
                return "請等待目前工作完成。";
            var queued = string.IsNullOrEmpty(QueuedTitle)
                ? ""
                : "已記下下一件：" + StripEllipsis(QueuedTitle) + "。完成後請再點一次。";
            return string.IsNullOrEmpty(queued)
                ? "目前：" + DisplayTitle + "。"
                : "目前：" + BarTitle + "。" + queued;
        }
    }

    public string WindowCaption(string? workspaceName)
    {
        if (!IsActive)
            return AppInfo.WindowTitle(workspaceName);
        var head = string.IsNullOrEmpty(ProgressText) ? Headline : Headline + " " + ProgressText;
        var name = string.IsNullOrWhiteSpace(workspaceName) ? AppInfo.Product : workspaceName.Trim();
        return head + " — " + name;
    }

    public OccupancySnapshot WithProgress(int done, int total, string? currentName) =>
        this with { Done = done, Total = total, CurrentName = currentName };

    public OccupancySnapshot WithQueued(string title) =>
        this with { QueuedTitle = title };

    public static OccupancySnapshot Begin(
        OccupancyKind kind,
        string title,
        string source,
        int pid,
        int total = 0,
        string? currentName = null) =>
        new(true, kind, title, source, pid, DateTimeOffset.UtcNow, 0, total, currentName, null);

    public static OccupancySnapshot FromRecord(OccupancyRecord record) =>
        new(
            true,
            OccupancyKindLookup.Parse(record.Kind),
            record.Title,
            string.IsNullOrWhiteSpace(record.Source) ? OccupancySources.Mcp : record.Source,
            record.Pid,
            record.StartedUtc,
            record.Done,
            record.Total,
            record.CurrentName,
            null);

    public OccupancyRecord ToRecord() => new()
    {
        Kind = Kind.ToString(),
        Title = Title,
        Source = Source,
        Pid = Pid,
        StartedUtc = StartedUtc,
        Done = Done,
        Total = Total,
        CurrentName = CurrentName,
    };

    public static OccupancyKind Infer(string title) => OccupancyKindLookup.Infer(title);

    static string StripEllipsis(string title)
    {
        var t = (title ?? "").Trim();
        return t.EndsWith('…') ? t[..^1] : t;
    }
}

public static class OccupancyKindLookup
{
    public static OccupancyKind Infer(string title)
    {
        var t = title ?? "";
        if (t.Contains("Agent", StringComparison.OrdinalIgnoreCase) && t.Contains("編譯"))
            return OccupancyKind.McpBuild;
        if (t.Contains("建置") || t.Contains("編譯"))
            return OccupancyKind.Build;
        if (t.Contains("測試"))
            return OccupancyKind.Test;
        if (t.Contains("掃描"))
            return OccupancyKind.Scan;
        if (t.Contains("同步") || t.Contains("提交") || t.Contains("分支") || t.Contains("PR") || t.Contains("Clone") || t.Contains("Git"))
            return OccupancyKind.Git;
        if (t.Contains("停止"))
            return OccupancyKind.Stop;
        if (t.Contains("啟動") || t.Contains("重啟"))
            return OccupancyKind.Start;
        if (t.Contains("更新") || t.Contains("下載"))
            return OccupancyKind.Update;
        if (t.Contains("發布") || t.Contains("打包") || t.Contains("發行") || t.Contains("Release"))
            return OccupancyKind.Pack;
        if (t.Contains("還原") || t.Contains("安裝"))
            return OccupancyKind.Other;
        return OccupancyKind.Other;
    }

    public static OccupancyKind Parse(string? value)
    {
        if (Enum.TryParse<OccupancyKind>(value, ignoreCase: true, out var kind))
            return kind;
        return OccupancyKind.Other;
    }
}

public sealed class OccupancyRecord
{
    public string Kind { get; set; } = nameof(OccupancyKind.Other);
    public string Title { get; set; } = "";
    public string Source { get; set; } = OccupancySources.Desktop;
    public int Pid { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string? CurrentName { get; set; }

    public string DisplayTitle
    {
        get
        {
            var head = Source == OccupancySources.Mcp || OccupancyKindLookup.Parse(Kind) == OccupancyKind.McpBuild
                ? "Agent 正在編譯"
                : (string.IsNullOrWhiteSpace(Title) ? "忙碌中" : Title.Trim().TrimEnd('…'));
            var progress = Total > 0 ? $" {Done}/{Total}" : "";
            var current = string.IsNullOrEmpty(CurrentName) ? "" : " · " + CurrentName;
            return head + progress + current;
        }
    }
}
