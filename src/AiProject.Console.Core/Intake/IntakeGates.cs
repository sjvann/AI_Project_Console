namespace AiProject.Console.Core.Intake;

public static class IntakeGates
{
    public static string? BlockDesignReady(IntakeRecord intake)
    {
        if (string.IsNullOrWhiteSpace(intake.Title))
            return "請先填標題。";
        if (string.IsNullOrWhiteSpace(intake.Body))
            return "請先寫說明。";
        if (string.IsNullOrWhiteSpace(intake.GithubSlug))
            return "請指定目標倉（owner/repo）。";
        if (intake.IsDesignChange)
        {
            if (string.IsNullOrWhiteSpace(intake.AsIs) || string.IsNullOrWhiteSpace(intake.ToBe))
                return "設計變更要填現況與期望。";
            if (string.IsNullOrWhiteSpace(intake.Impact))
                return "設計變更要填影響範圍。";
            if (intake.DesignDocs.All(string.IsNullOrWhiteSpace))
                return "設計變更要掛上設計或架構頁。";
        }
        return null;
    }

    public static string? BlockSplit(IntakeRecord intake)
    {
        var ready = BlockDesignReady(intake);
        if (ready is not null)
            return ready;
        if (intake.Items.Count == 0)
            return "至少拆一筆工作任務。";
        if (intake.Items.Any(i => string.IsNullOrWhiteSpace(i.Title)))
            return "每筆任務都要有標題。";
        return null;
    }

    public static string? BlockPublish(IntakeRecord intake)
    {
        var split = BlockSplit(intake);
        if (split is not null)
            return split;
        foreach (var item in intake.Items)
        {
            var criteria = item.AcceptanceCriteria.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (criteria.Count == 0)
                return $"「{item.Title}」沒有驗收條件，不能發出。";
        }
        return null;
    }

    public static string? BlockAccept(IntakeRecord intake)
    {
        if (intake.Items.Count == 0 || intake.Items.Any(i => !i.HasIssue))
            return "還有任務尚未發出 Issue。";
        foreach (var item in intake.Items)
        {
            var checks = item.Checks();
            if (checks.Count == 0 || checks.Any(c => !c.Done))
                return $"「{item.Title}」的驗收條件尚未全部勾選。";
            if (!string.IsNullOrEmpty(item.PrUrl) && !IsMerged(item.PrState))
                return $"「{item.Title}」的合併請求尚未入主線。";
        }
        if (!intake.SkipDeploy && string.IsNullOrWhiteSpace(intake.DeployRunId))
            return "尚未部署。請觸發部署，或明示本進件不部署。";
        return null;
    }

    public static string? BlockMerge(IntakeRecord intake)
    {
        var prs = intake.Items.Where(i => !string.IsNullOrEmpty(i.PrUrl)).ToList();
        if (prs.Count == 0)
            return "這個進件還沒有連結的合併請求。";
        if (prs.Any(i => i.CiTone == "warn"))
            return "CI 未通過，不能合併。";
        if (prs.Any(i => i.CiTone != "ok"))
            return "檢查還沒綠，不能合併。";
        return null;
    }

    static bool IsMerged(string? state) =>
        string.Equals(state, "MERGED", StringComparison.OrdinalIgnoreCase);

    public static bool CanPublish(IntakeRecord intake) => BlockPublish(intake) is null;

    public static bool CanAccept(IntakeRecord intake) => BlockAccept(intake) is null;
}
