namespace AiProject.Analysis.Domain;

public enum IssueKind
{
    Feature = 0,
    Test = 1,
    Acceptance = 2,
    Debug = 3,
}

/// <summary>一則待發／已發 Issue 草稿（PRD-SA-04）。</summary>
public sealed class IssueDraft
{
    public string LocalId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public IssueKind Kind { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = [];
    public IReadOnlyList<string> TracesRequirementIds { get; init; } = [];
    public IReadOnlyList<string> TracesSpecIds { get; init; } = [];
}

/// <summary>已寫到 GitHub（或 dry-run）的結果。</summary>
public sealed class PublishedIssue
{
    public string LocalId { get; init; } = "";
    public int? Number { get; init; }
    public string HtmlUrl { get; init; } = "";
    public string? Assignee { get; init; }
    public bool DryRun { get; init; }
}

/// <summary>Issue 包：草稿＋可選已發布結果。</summary>
public sealed class IssuePack
{
    public string Title { get; init; } = "";
    public string OwnerRepo { get; init; } = "";
    public IReadOnlyList<IssueDraft> Drafts { get; init; } = [];
    public IReadOnlyList<PublishedIssue> Published { get; private set; } = [];
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public void SetPublished(IReadOnlyList<PublishedIssue> published, DateTimeOffset at)
    {
        Published = published.ToList();
        PublishedAt = at;
    }

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# Issue 包：{Title}");
        sb.AppendLine();
        sb.AppendLine($"> 產出時間（UTC）：{GeneratedAt:yyyy-MM-dd HH:mm}");
        if (!string.IsNullOrWhiteSpace(OwnerRepo))
            sb.AppendLine($"> 目標倉：`{OwnerRepo}`");
        if (PublishedAt is { } publishedAt)
            sb.AppendLine($"> 發布時間（UTC）：{publishedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("## 草稿");
        sb.AppendLine();
        foreach (var d in Drafts)
        {
            sb.AppendLine($"### {d.LocalId} · {d.Title}");
            sb.AppendLine();
            sb.AppendLine($"- 類型：{d.Kind}");
            sb.AppendLine($"- 標籤：{string.Join(", ", d.Labels)}");
            sb.AppendLine($"- 追溯需求：{string.Join(", ", d.TracesRequirementIds)}");
            sb.AppendLine($"- 追溯規格：{string.Join(", ", d.TracesSpecIds)}");
            sb.AppendLine();
            sb.AppendLine(d.Body);
            sb.AppendLine();
        }

        if (Published.Count > 0)
        {
            sb.AppendLine("## 已發布");
            sb.AppendLine();
            foreach (var pub in Published)
            {
                var dry = pub.DryRun ? "（dry-run）" : "";
                var num = pub.Number is { } n ? $"#{n}" : "—";
                var who = string.IsNullOrWhiteSpace(pub.Assignee) ? "未指派" : pub.Assignee;
                sb.AppendLine($"- **{pub.LocalId}** {num} → {who} {dry}");
                if (!string.IsNullOrWhiteSpace(pub.HtmlUrl))
                    sb.AppendLine($"  - {pub.HtmlUrl}");
            }
        }

        return sb.ToString();
    }

    public object ToJsonShape() => new
    {
        kind = "issues",
        title = Title,
        ownerRepo = OwnerRepo,
        generatedAt = GeneratedAt,
        publishedAt = PublishedAt,
        drafts = Drafts.Select(d => new
        {
            localId = d.LocalId,
            title = d.Title,
            body = d.Body,
            kind = d.Kind.ToString(),
            labels = d.Labels,
            tracesRequirementIds = d.TracesRequirementIds,
            tracesSpecIds = d.TracesSpecIds,
        }).ToList(),
        published = Published.Select(p => new
        {
            localId = p.LocalId,
            number = p.Number,
            htmlUrl = p.HtmlUrl,
            assignee = p.Assignee,
            dryRun = p.DryRun,
        }).ToList(),
    };
}

public static partial class AnalysisArtifactFactory
{
    public static IssuePack BuildIssuePack(
        AnalysisCase analysisCase,
        RequirementsDocument requirements,
        SpecDocument spec,
        string ownerRepo,
        DateTimeOffset now)
    {
        var drafts = new List<IssueDraft>();
        var n = 1;

        foreach (var req in requirements.Requirements)
        {
            var relatedSpecs = spec.Items
                .Where(s => s.TracesRequirementIds.Contains(req.Id))
                .Select(s => s.Id)
                .ToList();
            drafts.Add(new IssueDraft
            {
                LocalId = $"ISSUE-{n:000}",
                Title = $"[{req.Id}] {Truncate(req.Statement, 72)}",
                Body = BuildFeatureBody(analysisCase, req, relatedSpecs),
                Kind = IssueKind.Feature,
                Labels = ["analysis", "feature", "needs-assignment"],
                TracesRequirementIds = [req.Id],
                TracesSpecIds = relatedSpecs,
            });
            n++;
        }

        drafts.Add(new IssueDraft
        {
            LocalId = $"ISSUE-{n:000}",
            Title = $"[測試] {analysisCase.Title} 驗收測試",
            Body = BuildTestBody(requirements),
            Kind = IssueKind.Test,
            Labels = ["analysis", "test"],
            TracesRequirementIds = requirements.Requirements.Select(r => r.Id).ToList(),
            TracesSpecIds = spec.Items.Where(i => i.Kind == "行為").Select(i => i.Id).Take(3).ToList(),
        });
        n++;

        drafts.Add(new IssueDraft
        {
            LocalId = $"ISSUE-{n:000}",
            Title = $"[驗收] {analysisCase.Title}",
            Body = BuildAcceptanceBody(requirements),
            Kind = IssueKind.Acceptance,
            Labels = ["analysis", "acceptance"],
            TracesRequirementIds = requirements.Requirements.Select(r => r.Id).Take(2).ToList(),
            TracesSpecIds = [],
        });
        n++;

        if (analysisCase.OpenQuestions.Count > 0 || analysisCase.Assumptions.Count > 0)
        {
            drafts.Add(new IssueDraft
            {
                LocalId = $"ISSUE-{n:000}",
                Title = $"[追蹤] {analysisCase.Title} 假設與待決",
                Body = BuildDebugBody(analysisCase),
                Kind = IssueKind.Debug,
                Labels = ["analysis", "debug", "assumptions"],
                TracesRequirementIds = requirements.Requirements.Select(r => r.Id).Take(1).ToList(),
                TracesSpecIds = [],
            });
        }

        return new IssuePack
        {
            Title = analysisCase.Title,
            OwnerRepo = ownerRepo.Trim(),
            Drafts = drafts,
            GeneratedAt = now,
        };
    }

    static string BuildFeatureBody(AnalysisCase c, RequirementItem req, IReadOnlyList<string> specs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 背景");
        sb.AppendLine();
        sb.AppendLine(Truncate(c.ConceptText, 400));
        sb.AppendLine();
        sb.AppendLine("## 需求");
        sb.AppendLine();
        sb.AppendLine($"- **{req.Id}**（{req.Priority}）：{req.Statement}");
        if (specs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 追溯規格");
            sb.AppendLine();
            foreach (var s in specs)
                sb.AppendLine($"- {s}");
        }
        sb.AppendLine();
        sb.AppendLine("## 假設（必須可見）");
        sb.AppendLine();
        foreach (var a in c.Assumptions)
            sb.AppendLine($"- {a}");
        sb.AppendLine();
        sb.AppendLine("## 驗收 checklist");
        sb.AppendLine();
        sb.AppendLine("- [ ] 行為符合需求敘述");
        sb.AppendLine("- [ ] 假設未 silently 當成已確認");
        sb.AppendLine("- [ ] 相關測試通過");
        return sb.ToString();
    }

    static string BuildTestBody(RequirementsDocument req)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 測試工作項");
        sb.AppendLine();
        sb.AppendLine("依驗收條件與需求條目撰寫／執行測試。");
        sb.AppendLine();
        sb.AppendLine("### 驗收條件");
        sb.AppendLine();
        foreach (var a in req.AcceptanceCriteria)
            sb.AppendLine($"- [ ] {a}");
        sb.AppendLine();
        sb.AppendLine("### 需求覆蓋");
        sb.AppendLine();
        foreach (var r in req.Requirements)
            sb.AppendLine($"- [ ] {r.Id}");
        return sb.ToString();
    }

    static string BuildAcceptanceBody(RequirementsDocument req)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 驗收");
        sb.AppendLine();
        sb.AppendLine("窗口／PM 確認範圍與非範圍後勾選。");
        sb.AppendLine();
        sb.AppendLine("### 範圍");
        foreach (var x in req.InScope)
            sb.AppendLine($"- [ ] {x}");
        sb.AppendLine();
        sb.AppendLine("### 非範圍（確認不做）");
        foreach (var x in req.OutOfScope)
            sb.AppendLine($"- [ ] {x}");
        return sb.ToString();
    }

    static string BuildDebugBody(AnalysisCase c)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 假設與待決追蹤（PRD-SA-06）");
        sb.AppendLine();
        sb.AppendLine("### 假設");
        foreach (var a in c.Assumptions)
            sb.AppendLine($"- [ ] 已確認或改寫：{a}");
        sb.AppendLine();
        sb.AppendLine("### 待決");
        if (c.OpenQuestions.Count == 0)
            sb.AppendLine("- （無）");
        else
            foreach (var q in c.OpenQuestions)
                sb.AppendLine($"- [ ] 已關閉：{q}");
        return sb.ToString();
    }
}
