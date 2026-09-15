namespace AiProject.Analysis.Domain;

/// <summary>一則可追溯需求（PRD-SA-02／SA-03）。</summary>
public sealed record RequirementItem(string Id, string Statement, string Priority);

/// <summary>一則規格條目，必須追溯至少一個需求 ID。</summary>
public sealed record SpecItem(string Id, string Kind, string Statement, IReadOnlyList<string> TracesRequirementIds);

/// <summary>需求文件產物（Markdown／JSON 同源）。</summary>
public sealed class RequirementsDocument
{
    public string Title { get; init; } = "";
    public string Problem { get; init; } = "";
    public IReadOnlyList<string> Stakeholders { get; init; } = [];
    public IReadOnlyList<string> InScope { get; init; } = [];
    public IReadOnlyList<string> OutOfScope { get; init; } = [];
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public IReadOnlyList<RequirementItem> Requirements { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; }

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 需求文件：{Title}");
        sb.AppendLine();
        sb.AppendLine($"> 產出時間（UTC）：{GeneratedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine("> 對齊 ISO/IEC/IEEE 29148 需求工程**概念**；本產物不宣稱已認證合規。");
        sb.AppendLine();
        sb.AppendLine("## 問題／痛點");
        sb.AppendLine();
        sb.AppendLine(Problem);
        sb.AppendLine();
        sb.AppendLine("## 干係人");
        sb.AppendLine();
        AppendBullets(sb, Stakeholders);
        sb.AppendLine();
        sb.AppendLine("## 範圍");
        sb.AppendLine();
        AppendBullets(sb, InScope);
        sb.AppendLine();
        sb.AppendLine("## 非範圍");
        sb.AppendLine();
        AppendBullets(sb, OutOfScope);
        sb.AppendLine();
        sb.AppendLine("## 驗收條件");
        sb.AppendLine();
        AppendBullets(sb, AcceptanceCriteria);
        sb.AppendLine();
        sb.AppendLine("## 需求條目");
        sb.AppendLine();
        foreach (var r in Requirements)
            sb.AppendLine($"- **{r.Id}**（{r.Priority}）：{r.Statement}");
        sb.AppendLine();
        sb.AppendLine("## 假設（必須可見 · PRD-SA-06）");
        sb.AppendLine();
        AppendBullets(sb, Assumptions);
        sb.AppendLine();
        sb.AppendLine("## 待決問題");
        sb.AppendLine();
        if (OpenQuestions.Count == 0)
            sb.AppendLine("- （無）");
        else
            AppendBullets(sb, OpenQuestions);
        return sb.ToString();
    }

    public object ToJsonShape() => new
    {
        kind = "requirements",
        title = Title,
        generatedAt = GeneratedAt,
        problem = Problem,
        stakeholders = Stakeholders,
        inScope = InScope,
        outOfScope = OutOfScope,
        acceptanceCriteria = AcceptanceCriteria,
        assumptions = Assumptions,
        openQuestions = OpenQuestions,
        requirements = Requirements.Select(r => new { id = r.Id, statement = r.Statement, priority = r.Priority }).ToList(),
    };

    static void AppendBullets(System.Text.StringBuilder sb, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            sb.AppendLine("- （待補）");
            return;
        }
        foreach (var item in items)
            sb.AppendLine($"- {item}");
    }
}

/// <summary>規格文件產物（行為／介面／資料／非功能；追溯需求 ID）。</summary>
public sealed class SpecDocument
{
    public string Title { get; init; } = "";
    public IReadOnlyList<SpecItem> Items { get; init; } = [];
    public IReadOnlyList<string> Assumptions { get; init; } = [];
    public IReadOnlyList<string> OpenQuestions { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; }

    public string ToMarkdown()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# 規格摘要：{Title}");
        sb.AppendLine();
        sb.AppendLine($"> 產出時間（UTC）：{GeneratedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine("> 語意可對映 ReqIF／OSLC RM；第一刀不做完整 ReqIF 伺服器。");
        sb.AppendLine();
        sb.AppendLine("## 規格條目");
        sb.AppendLine();
        foreach (var item in Items)
        {
            var traces = string.Join(", ", item.TracesRequirementIds);
            sb.AppendLine($"- **{item.Id}**〔{item.Kind}〕← {traces}");
            sb.AppendLine($"  - {item.Statement}");
        }
        sb.AppendLine();
        sb.AppendLine("## 假設（必須可見 · PRD-SA-06）");
        sb.AppendLine();
        if (Assumptions.Count == 0)
            sb.AppendLine("- （無）");
        else
            foreach (var a in Assumptions)
                sb.AppendLine($"- {a}");
        sb.AppendLine();
        sb.AppendLine("## 待決問題");
        sb.AppendLine();
        if (OpenQuestions.Count == 0)
            sb.AppendLine("- （無）");
        else
            foreach (var q in OpenQuestions)
                sb.AppendLine($"- {q}");
        return sb.ToString();
    }

    public object ToJsonShape() => new
    {
        kind = "spec",
        title = Title,
        generatedAt = GeneratedAt,
        assumptions = Assumptions,
        openQuestions = OpenQuestions,
        items = Items.Select(i => new
        {
            id = i.Id,
            kind = i.Kind,
            statement = i.Statement,
            tracesRequirementIds = i.TracesRequirementIds,
        }).ToList(),
    };
}

/// <summary>從分析案組出需求／規格（規則模板；B2 不呼叫外部 AI）。</summary>
public static partial class AnalysisArtifactFactory
{
    public static RequirementsDocument BuildRequirements(
        AnalysisCase analysisCase,
        DateTimeOffset now,
        IEnumerable<string>? stakeholders = null,
        IEnumerable<string>? inScope = null,
        IEnumerable<string>? outOfScope = null,
        IEnumerable<string>? acceptance = null)
    {
        var stake = Normalize(stakeholders);
        if (stake.Count == 0)
            stake = ["需求窗口", "系統分析／PM", "實作工程師"];

        var scope = Normalize(inScope);
        if (scope.Count == 0)
            scope =
            [
                "釐清問題與期望結果",
                "產出可讀需求文件與可追溯規格摘要",
            ];

        var outScope = Normalize(outOfScope);
        if (outScope.Count == 0)
            outScope =
            [
                "不在本產物內實作系統或代管原始碼",
                "不做自動媒合、派遣或金流",
            ];

        var accept = Normalize(acceptance);
        if (accept.Count == 0)
            accept =
            [
                "窗口能讀懂範圍／非範圍／驗收",
                "每條需求有 ID，可供規格追溯",
                "假設與待決問題寫在產物內且可見",
            ];

        var requirements = new List<RequirementItem>
        {
            new("REQ-001", $"系統應能處理此痛點：{Truncate(analysisCase.ConceptText, 160)}", "高"),
            new("REQ-002", "系統應明確標示範圍與非範圍，避免範圍蔓延。", "高"),
            new("REQ-003", "系統應提供可驗證的驗收條件。", "中"),
        };
        if (analysisCase.OpenQuestions.Count > 0)
            requirements.Add(new("REQ-004", "系統應追蹤待決問題直到關閉或明確接受為假設。", "中"));

        return new RequirementsDocument
        {
            Title = analysisCase.Title,
            Problem = analysisCase.ConceptText,
            Stakeholders = stake,
            InScope = scope,
            OutOfScope = outScope,
            AcceptanceCriteria = accept,
            Assumptions = analysisCase.Assumptions.ToList(),
            OpenQuestions = analysisCase.OpenQuestions.ToList(),
            Requirements = requirements,
            GeneratedAt = now,
        };
    }

    public static SpecDocument BuildSpec(AnalysisCase analysisCase, RequirementsDocument requirements, DateTimeOffset now)
    {
        if (requirements.Requirements.Count == 0)
            throw new AnalysisException("no-requirements", "沒有需求條目，無法產出規格。");

        var items = new List<SpecItem>();
        var n = 1;
        foreach (var req in requirements.Requirements)
        {
            items.Add(new SpecItem(
                $"SPEC-{n:000}",
                "行為",
                $"對應 {req.Id}：實作須滿足「{req.Statement}」。",
                [req.Id]));
            n++;
        }

        items.Add(new SpecItem(
            $"SPEC-{n:000}",
            "介面",
            "產物須可匯出 Markdown 與結構化 JSON，供人讀與機讀。",
            requirements.Requirements.Select(r => r.Id).Take(2).ToList()));
        n++;
        items.Add(new SpecItem(
            $"SPEC-{n:000}",
            "非功能",
            "假設與待決問題必須在產物中可見，不可 silently 當成已確認需求。",
            ["REQ-002", requirements.Requirements[0].Id]));

        return new SpecDocument
        {
            Title = analysisCase.Title,
            Items = items,
            Assumptions = analysisCase.Assumptions.ToList(),
            OpenQuestions = analysisCase.OpenQuestions.ToList(),
            GeneratedAt = now,
        };
    }

    static List<string> Normalize(IEnumerable<string>? raw) =>
        (raw ?? [])
        .Select(x => x.Trim())
        .Where(x => x.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}
