namespace AiProject.Console.Core.Intake;

public static class IntakeKinds
{
    public const string Requirement = "requirement";
    public const string DesignChange = "design-change";

    public static string Label(string? kind) =>
        kind == DesignChange ? "設計變更" : "需求";
}

public static class IntakeHolds
{
    public const string Paused = "paused";
    public const string Recalled = "recalled";

    public static string Label(string? hold) =>
        hold switch
        {
            Paused => "已暫停",
            Recalled => "已收回",
            _ => "",
        };
}

public sealed class IntakeVisual
{
    public string Path { get; set; } = "";
    public string Note { get; set; } = "";
}

public static class IntakeStages
{
    public const string Draft = "draft";
    public const string DesignReady = "design";
    public const string Split = "split";
    public const string Issued = "issued";
    public const string Doing = "doing";
    public const string Verify = "verify";
    public const string Review = "review";
    public const string Merged = "merged";
    public const string Released = "released";
    public const string Deployed = "deployed";
    public const string Accepted = "accepted";
    public const string Billed = "billed";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        (Draft, "草稿"),
        (DesignReady, "設計就緒"),
        (Split, "已拆解"),
        (Issued, "已發出"),
        (Doing, "實作中"),
        (Verify, "驗證中"),
        (Review, "可審查"),
        (Merged, "已入主線"),
        (Released, "已發行"),
        (Deployed, "已部署"),
        (Accepted, "已驗收"),
        (Billed, "已報帳"),
    ];

    public static string Label(string? stage)
    {
        var found = All.FirstOrDefault(s => s.Id == stage);
        return string.IsNullOrEmpty(found.Label) ? "草稿" : found.Label;
    }

    public static int IndexOf(string? stage)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == stage)
                return i;
        }
        return 0;
    }
}

public sealed class IntakeWorkItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> AcceptanceCriteria { get; set; } = [];
    public List<bool> AcceptanceDone { get; set; } = [];
    public string Assignee { get; set; } = "";
    public string GithubSlug { get; set; } = "";
    public int? IssueNumber { get; set; }
    public string IssueUrl { get; set; } = "";
    public string PrNumber { get; set; } = "";
    public string PrUrl { get; set; } = "";
    public string PrState { get; set; } = "";
    public string CiTone { get; set; } = "";
    public string CiHint { get; set; } = "";

    public bool HasIssue => IssueNumber is > 0;

    public IReadOnlyList<(string Text, bool Done)> Checks()
    {
        var list = new List<(string, bool)>();
        for (var i = 0; i < AcceptanceCriteria.Count; i++)
        {
            var done = i < AcceptanceDone.Count && AcceptanceDone[i];
            list.Add((AcceptanceCriteria[i], done));
        }
        return list;
    }
}

public sealed class IntakeRecord
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = IntakeKinds.Requirement;
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string ProductLine { get; set; } = "";
    public string GithubSlug { get; set; } = "";
    public string Host { get; set; } = "";
    public string Priority { get; set; } = "normal";
    public string Requester { get; set; } = "";
    public string OpenedOn { get; set; } = "";
    public string AsIs { get; set; } = "";
    public string ToBe { get; set; } = "";
    public string Impact { get; set; } = "";
    public List<string> DesignDocs { get; set; } = [];
    public string Stage { get; set; } = IntakeStages.Draft;
    public string BlockReason { get; set; } = "";
    public List<IntakeWorkItem> Items { get; set; } = [];
    public string ReleaseTag { get; set; } = "";
    public string DeployRunId { get; set; } = "";
    public string DeployHint { get; set; } = "";
    public string? AcceptedAt { get; set; }
    public bool SkipDeploy { get; set; }
    public bool Billed { get; set; }
    public bool IsUi { get; set; }
    public string Hold { get; set; } = "";
    public string HoldNote { get; set; } = "";
    public string HoldAt { get; set; } = "";
    public string HoldBy { get; set; } = "";
    public List<IntakeVisual> Sketches { get; set; } = [];
    public List<IntakeVisual> Crops { get; set; } = [];

    public bool IsDesignChange => Kind == IntakeKinds.DesignChange;

    public bool IsPaused => Hold == IntakeHolds.Paused;

    public bool IsRecalled => Hold == IntakeHolds.Recalled;

    public string KindLabel => IntakeKinds.Label(Kind);

    public string StageLabel => IntakeStages.Label(Stage);

    public string HoldLabel => IntakeHolds.Label(Hold);

    public int IssuedCount => Items.Count(i => i.HasIssue);

    public int ClosedIssueGuess => Items.Count(i => i.HasIssue && string.Equals(i.PrState, "MERGED", StringComparison.OrdinalIgnoreCase));
}

public sealed class IntakeDocument
{
    public string Version { get; set; } = "1";
    public List<IntakeRecord> Intakes { get; set; } = [];
}

public sealed record IssueTrace(
    int Number,
    string Title,
    string State,
    string Url,
    IReadOnlyList<string> Assignees,
    string PrUrl = "",
    string PrState = "",
    string CiTone = "",
    string CiHint = "",
    bool ChecksGreen = false,
    bool Merged = false);
