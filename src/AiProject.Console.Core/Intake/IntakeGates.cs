namespace AiProject.Console.Core.Intake;

public static class IntakeGates
{
    public static string? BlockPublish(IntakeRecord intake)
    {
        if (string.IsNullOrWhiteSpace(intake.Title))
            return "請先填標題。";
        if (string.IsNullOrWhiteSpace(intake.Body))
            return "請先寫說明。";
        if (string.IsNullOrWhiteSpace(intake.GithubSlug))
            return "請指定目標倉（owner/repo）。";
        return null;
    }

    public static string? BlockAccept(IntakeRecord intake)
    {
        if (intake.IssuedCount == 0)
            return "尚未發出 Issue。";
        return null;
    }

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
