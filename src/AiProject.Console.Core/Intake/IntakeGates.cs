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
        if (intake.IsUi)
        {
            if (intake.IsDesignChange)
            {
                var crops = intake.Crops.Where(c => !string.IsNullOrWhiteSpace(c.Path)).ToList();
                if (crops.Count == 0)
                    return "介面設計變更要提供剪圖，標出修改處。";
                if (crops.Any(c => string.IsNullOrWhiteSpace(c.Note)))
                    return "每張剪圖都要寫修改說明。";
            }
            else if (intake.Sketches.All(s => string.IsNullOrWhiteSpace(s.Path)))
                return "介面新需求要附一張草圖。";
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

    /// <summary>尚未發出遠端 Issue 的進件可刪；已發出的走收回／暫停。</summary>
    public static bool CanDelete(IntakeRecord intake) =>
        intake.IssuedCount == 0 && !intake.IsRecalled;

    /// <summary>已發出、尚未驗收／報帳，且未收回。</summary>
    public static bool CanHold(IntakeRecord intake) =>
        intake.IssuedCount > 0
        && !intake.IsRecalled
        && string.IsNullOrEmpty(intake.AcceptedAt)
        && !intake.Billed;

    public static bool CanPause(IntakeRecord intake) => CanHold(intake) && !intake.IsPaused;

    public static bool CanResume(IntakeRecord intake) => intake.IsPaused && !intake.IsRecalled;

    public static bool CanRecall(IntakeRecord intake) => CanHold(intake);

    public static bool HasMergedWork(IntakeRecord intake) =>
        intake.Items.Any(i => string.Equals(i.PrState, "MERGED", StringComparison.OrdinalIgnoreCase));

    public static string? BlockWhileHeld(IntakeRecord intake)
    {
        if (intake.IsRecalled)
            return "此進件已收回。";
        if (intake.IsPaused)
            return "此進件已暫停執行，請先恢復。";
        return null;
    }
}
