using System.Text.Json.Serialization;

namespace AiProject.Console.Core.Intake;

public static class IntakeKinds
{
    public const string Issue = "issue";
    public const string Requirement = "requirement";
    public const string DesignChange = "design-change";

    public static string Label(string? kind) =>
        kind switch
        {
            DesignChange => "設計變更",
            Requirement => "需求",
            _ => "Issue",
        };
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
    public const string Issued = "issued";
    public const string Doing = "doing";
    public const string Accepted = "accepted";

    public const string DesignReady = "design";
    public const string Split = "split";
    public const string Verify = "verify";
    public const string Review = "review";
    public const string Merged = "merged";
    public const string Released = "released";
    public const string Deployed = "deployed";
    public const string Billed = "billed";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        (Draft, "草稿"),
        (Issued, "已發出"),
        (Doing, "實作中"),
        (Accepted, "已驗收"),
    ];

    public static string Canonical(string? stage) =>
        stage switch
        {
            Issued => Issued,
            Doing => Doing,
            Accepted or Billed => Accepted,
            Verify or Review or Merged or Released or Deployed => Doing,
            DesignReady or Split or Draft => Draft,
            _ => Draft,
        };

    public static string Label(string? stage)
    {
        var id = Canonical(stage);
        var found = All.FirstOrDefault(s => s.Id == id);
        return string.IsNullOrEmpty(found.Label) ? "草稿" : found.Label;
    }

    public static int IndexOf(string? stage)
    {
        var id = Canonical(stage);
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id)
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
    public string Kind { get; set; } = IntakeKinds.Issue;
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

    [JsonIgnore]
    public bool IsDesignChange => Kind == IntakeKinds.DesignChange;

    [JsonIgnore]
    public string Assignee
    {
        get => Items.Count > 0 ? Items[0].Assignee : "";
        set => EnsurePrimaryItem().Assignee = value ?? "";
    }

    [JsonIgnore]
    public string AssigneeText => string.IsNullOrWhiteSpace(Assignee) ? "未指派" : Assignee.Trim();

    [JsonIgnore]
    public IntakeWorkItem PrimaryItem => EnsurePrimaryItem();

    [JsonIgnore]
    public bool IsPaused => Hold == IntakeHolds.Paused;

    [JsonIgnore]
    public bool IsRecalled => Hold == IntakeHolds.Recalled;

    [JsonIgnore]
    public string KindLabel => IntakeKinds.Label(Kind);

    [JsonIgnore]
    public string StageLabel => IntakeStages.Label(Stage);

    [JsonIgnore]
    public string HoldLabel => IntakeHolds.Label(Hold);

    [JsonIgnore]
    public int IssuedCount => Items.Count(i => i.HasIssue);

    [JsonIgnore]
    public int ClosedIssueGuess => Items.Count(i => i.HasIssue && string.Equals(i.PrState, "MERGED", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool CountsAsIssued => IssuedCount > 0 && !IsRecalled;

    [JsonIgnore]
    public bool CountsAsPendingAcceptance => CountsAsIssued && string.IsNullOrEmpty(AcceptedAt);

    public IntakeWorkItem EnsurePrimaryItem()
    {
        if (Items.Count == 0)
        {
            Items.Add(new IntakeWorkItem
            {
                Id = "WI-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            });
        }
        var item = Items[0];
        if (!string.IsNullOrWhiteSpace(Title))
            item.Title = Title;
        if (!string.IsNullOrWhiteSpace(GithubSlug))
            item.GithubSlug = GithubSlug;
        return item;
    }

    public void MergeAttachments()
    {
        if (Crops.Count == 0)
            return;
        foreach (var crop in Crops.Where(c => !string.IsNullOrWhiteSpace(c.Path)))
        {
            if (!Sketches.Any(s => string.Equals(s.Path, crop.Path, StringComparison.OrdinalIgnoreCase)))
                Sketches.Add(crop);
        }
        Crops.Clear();
    }
}

public sealed class IntakeDocument
{
    public string Version { get; set; } = "1";
    public string DesignDocsDir { get; set; } = IntakeDesignFiles.DefaultDir;
    public List<IntakeRecord> Intakes { get; set; } = [];
}

public sealed record IntakeIssueLinks(string WebUrl, string Branch);

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
