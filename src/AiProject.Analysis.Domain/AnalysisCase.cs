namespace AiProject.Analysis.Domain;

public enum AnalysisCaseStatus
{
    Concept = 0,
    Requirements = 1,
    Spec = 2,
    Issues = 3,
}

/// <summary>一則分析案：概念 → 需求 → 規格 → Issue（B1 先到概念）。</summary>
public sealed class AnalysisCase
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string ConceptText { get; private set; } = "";
    public AnalysisCaseStatus Status { get; private set; }
    public List<string> Assumptions { get; private set; } = [];
    public List<string> OpenQuestions { get; private set; } = [];
    public RequirementsDocument? Requirements { get; private set; }
    public SpecDocument? Spec { get; private set; }
    public IssuePack? Issues { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static AnalysisCase Start(string title, string conceptText, DateTimeOffset now, IEnumerable<string>? assumptions = null, IEnumerable<string>? openQuestions = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new AnalysisException("required", "請填標題。");
        if (string.IsNullOrWhiteSpace(conceptText))
            throw new AnalysisException("required", "請描述需求概念或痛點。");
        var assumptionsList = (assumptions ?? [])
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var questions = (openQuestions ?? [])
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        // PRD-SA-06：AI／分析假設必須可見；空站也至少留一條提醒。
        if (assumptionsList.Count == 0)
            assumptionsList.Add("（待標註）本段概念尚未經窗口確認，不可當成已定需求。");
        return new AnalysisCase
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            ConceptText = conceptText.Trim(),
            Status = AnalysisCaseStatus.Concept,
            Assumptions = assumptionsList,
            OpenQuestions = questions,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public string StatusLabel => Status switch
    {
        AnalysisCaseStatus.Concept => "概念",
        AnalysisCaseStatus.Requirements => "需求文件",
        AnalysisCaseStatus.Spec => "規格",
        AnalysisCaseStatus.Issues => "Issue",
        _ => Status.ToString(),
    };

    /// <summary>B2：產出需求文件；假設／待決寫進產物（PRD-SA-02／06）。</summary>
    public RequirementsDocument ProduceRequirements(
        DateTimeOffset now,
        IEnumerable<string>? stakeholders = null,
        IEnumerable<string>? inScope = null,
        IEnumerable<string>? outOfScope = null,
        IEnumerable<string>? acceptance = null)
    {
        if (Status is AnalysisCaseStatus.Issues)
            throw new AnalysisException("bad-state", "已進入 Issue 階段，請另開新案調整需求。");
        var doc = AnalysisArtifactFactory.BuildRequirements(this, now, stakeholders, inScope, outOfScope, acceptance);
        Requirements = doc;
        Spec = null;
        Issues = null;
        Status = AnalysisCaseStatus.Requirements;
        UpdatedAt = now;
        return doc;
    }

    /// <summary>B2：依需求文件產出規格摘要（PRD-SA-03）。</summary>
    public SpecDocument ProduceSpec(DateTimeOffset now)
    {
        if (Requirements is null)
            throw new AnalysisException("need-requirements", "請先產出需求文件。");
        if (Status is AnalysisCaseStatus.Issues)
            throw new AnalysisException("bad-state", "已進入 Issue 階段，請另開新案調整規格。");
        var doc = AnalysisArtifactFactory.BuildSpec(this, Requirements, now);
        Spec = doc;
        Issues = null;
        Status = AnalysisCaseStatus.Spec;
        UpdatedAt = now;
        return doc;
    }

    /// <summary>B3：依需求＋規格組 Issue 包（尚未寫入 GitHub）。</summary>
    public IssuePack PrepareIssues(string ownerRepo, DateTimeOffset now)
    {
        if (Requirements is null || Spec is null)
            throw new AnalysisException("need-spec", "請先產出需求文件與規格摘要。");
        if (Status is AnalysisCaseStatus.Issues && Issues?.Published.Count > 0)
            throw new AnalysisException("already-published", "Issue 已發布；請另開新案。");
        if (string.IsNullOrWhiteSpace(ownerRepo))
            throw new AnalysisException("required", "請設定目標 GitHub 倉（owner/repo）。");
        var pack = AnalysisArtifactFactory.BuildIssuePack(this, Requirements, Spec, ownerRepo, now);
        Issues = pack;
        UpdatedAt = now;
        return pack;
    }

    /// <summary>B3：標記已發布（含 dry-run）。</summary>
    public void MarkIssuesPublished(IReadOnlyList<PublishedIssue> published, DateTimeOffset now)
    {
        if (Issues is null)
            throw new AnalysisException("need-issues", "請先組出 Issue 包。");
        if (published.Count == 0)
            throw new AnalysisException("required", "沒有已發布的 Issue。");
        Issues.SetPublished(published, now);
        Status = AnalysisCaseStatus.Issues;
        UpdatedAt = now;
    }
}

public sealed class AnalysisException : Exception
{
    public string Code { get; }

    public AnalysisException(string code, string message) : base(message) => Code = code;
}

public interface IAnalysisCaseStore
{
    Task AddAsync(AnalysisCase item, CancellationToken ct = default);
    Task UpdateAsync(AnalysisCase item, CancellationToken ct = default);
    Task<AnalysisCase?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AnalysisCase>> ListAsync(CancellationToken ct = default);
}
