namespace AiProject.Console.Core.GitHub;

public enum ReleaseStepStatus
{
    Pending,
    Active,
    Done,
    Error,
    Skipped,
}

public sealed class ReleaseStepState
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Hint { get; init; }
    public ReleaseStepStatus Status { get; set; }
}

/// <summary>
/// 發行／打包進度。步驟與紀錄給進度視窗用，避免使用者只看到底部一小行。
/// </summary>
public sealed class ReleaseRunState
{
    public const string StagePrefix = "PACKSTAGE:";
    const int MaxLog = 50_000;

    string? _lastUser;

    ReleaseRunState(string title, IReadOnlyList<ReleaseStepState> steps)
    {
        Title = title;
        Steps = steps;
    }

    public string Title { get; }
    public IReadOnlyList<ReleaseStepState> Steps { get; }
    public string Log { get; private set; } = "";
    public bool Busy { get; private set; } = true;
    public string Headline { get; private set; } = "";
    public string Tone { get; private set; } = "info";
    public string StatusText { get; private set; } = "";
    public DateTimeOffset StepStarted { get; private set; } = DateTimeOffset.UtcNow;

    public string ElapsedText
    {
        get
        {
            if (!Busy)
                return "";
            var s = Math.Max(0, (int)(DateTimeOffset.UtcNow - StepStarted).TotalSeconds);
            return s < 60 ? $"已過 {s} 秒" : $"已過 {s / 60} 分 {s % 60} 秒";
        }
    }

    public string CopyText
    {
        get
        {
            var head = string.IsNullOrWhiteSpace(Headline) ? Title : Headline;
            return string.IsNullOrWhiteSpace(Log) ? head : head + "\n\n" + Log;
        }
    }

    public static ReleaseRunState PackOnly() =>
        Create("打包 Windows 安裝包", includeUpload: false, skipPack: false);

    public static ReleaseRunState PackAndPublish() =>
        Create("打包並發行 Release", includeUpload: true, skipPack: false);

    public static ReleaseRunState PublishOnly() =>
        Create("發行 Release", includeUpload: true, skipPack: true);

    public static string StageLine(string stageId, string message) =>
        StagePrefix + stageId + "|" + message;

    public void Begin()
    {
        Busy = true;
        Tone = "info";
        Headline = "";
        var first = Steps.FirstOrDefault(s => s.Status != ReleaseStepStatus.Skipped);
        if (first is not null)
            Activate(first.Id);
    }

    public void Apply(string? line)
    {
        var t = (line ?? "").Trim();
        if (t.Length == 0)
            return;
        if (t.StartsWith(StagePrefix, StringComparison.Ordinal))
        {
            var rest = t[StagePrefix.Length..];
            var i = rest.IndexOf('|');
            var id = (i < 0 ? rest : rest[..i]).Trim();
            var msg = (i < 0 ? rest : rest[(i + 1)..]).Trim();
            if (!string.IsNullOrEmpty(id))
                Activate(id);
            if (!string.IsNullOrEmpty(msg))
                AppendUser(msg);
            return;
        }

        AppendUser(t);
    }

    public void Succeed(string? message)
    {
        foreach (var step in Steps)
        {
            if (step.Status is ReleaseStepStatus.Pending or ReleaseStepStatus.Active)
                step.Status = ReleaseStepStatus.Done;
        }
        Busy = false;
        Tone = "ok";
        var text = string.IsNullOrWhiteSpace(message) ? "完成。" : message.Trim();
        Headline = FirstLine(text);
        StatusText = Headline;
        AppendUser(text);
    }

    public void Fail(string? message)
    {
        var active = Steps.FirstOrDefault(s => s.Status == ReleaseStepStatus.Active);
        if (active is not null)
            active.Status = ReleaseStepStatus.Error;
        foreach (var step in Steps)
        {
            if (step.Status == ReleaseStepStatus.Pending)
                step.Status = ReleaseStepStatus.Skipped;
        }
        Busy = false;
        Tone = "error";
        var text = string.IsNullOrWhiteSpace(message) ? "發生錯誤。" : message.Trim();
        Headline = FirstLine(text);
        StatusText = "錯誤";
        AppendUser(text);
    }

    void Activate(string id)
    {
        var idx = IndexOf(id);
        if (idx < 0)
            return;
        var changed = false;
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (i < idx)
            {
                if (step.Status is ReleaseStepStatus.Pending or ReleaseStepStatus.Active)
                {
                    step.Status = ReleaseStepStatus.Done;
                    changed = true;
                }
            }
            else if (i == idx && step.Status != ReleaseStepStatus.Error)
            {
                if (step.Status != ReleaseStepStatus.Active)
                {
                    step.Status = ReleaseStepStatus.Active;
                    StepStarted = DateTimeOffset.UtcNow;
                    StatusText = step.Label;
                    changed = true;
                }
            }
        }
        if (changed)
            _lastUser = null;
    }

    int IndexOf(string id)
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            if (string.Equals(Steps[i].Id, id, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    void AppendUser(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
            return;
        if (string.Equals(t, _lastUser, StringComparison.Ordinal))
            return;
        _lastUser = t;
        if (!Busy)
            StatusText = FirstLine(t);
        else if (!t.StartsWith("PACKSTAGE:", StringComparison.Ordinal))
            StatusText = FirstLine(t);
        Log = string.IsNullOrEmpty(Log) ? t : Log + "\n" + t;
        if (Log.Length <= MaxLog)
            return;
        Log = "…（略過較早紀錄）\n" + Log[^(MaxLog - 24)..];
    }

    static string FirstLine(string text)
    {
        var t = text.Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }

    static ReleaseRunState Create(string title, bool includeUpload, bool skipPack)
    {
        var steps = new List<ReleaseStepState>
        {
            new()
            {
                Id = "version",
                Label = "寫入版號",
                Hint = "把版號寫進 AppInfo、專案與安裝腳本。",
            },
            new()
            {
                Id = "inno",
                Label = "檢查 Inno Setup",
                Hint = "編譯安裝程式需要 ISCC.exe。",
            },
            new()
            {
                Id = "publish",
                Label = "編譯 Windows 執行檔",
                Hint = "dotnet publish，通常要 1–3 分鐘；這段可能暫時沒有新行。",
            },
            new()
            {
                Id = "zip",
                Label = "壓縮免安裝包",
                Hint = "產生 dist 裡的 zip。",
            },
            new()
            {
                Id = "installer",
                Label = "編譯安裝程式",
                Hint = "Inno Setup 產出 setup.exe。",
            },
        };
        if (includeUpload)
        {
            steps.Add(new ReleaseStepState
            {
                Id = "upload",
                Label = "上傳到 GitHub",
                Hint = "建立 Release 並附加安裝包，視檔案大小與網速而定。",
            });
        }
        if (skipPack)
        {
            foreach (var step in steps)
            {
                if (step.Id != "upload")
                    step.Status = ReleaseStepStatus.Skipped;
            }
        }
        return new ReleaseRunState(title, steps);
    }
}
