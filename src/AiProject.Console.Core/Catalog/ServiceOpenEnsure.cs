namespace AiProject.Console.Core.Catalog;

/// <summary>
/// 「開啟」前先確保行程就緒：可開 URL 的目標會進 <see cref="ProcessOps.ProcessSupervisor.StartTargetsAsync"/>，
/// 已就緒者被略過；本型別只判斷結果是否允許開瀏覽器。
/// </summary>
public static class ServiceOpenEnsure
{
    /// <summary>
    /// 目標本身若在 results 裡帶錯誤（含硬相依未就緒），不可開 URL。
    /// 無列或 Error 為 null＝可開（含原本已就緒、未寫入 results）。
    /// </summary>
    public static bool CanOpenUrl(
        string targetId,
        IReadOnlyList<(string Id, string Label, string? Error)> results)
    {
        if (string.IsNullOrWhiteSpace(targetId))
            return false;
        foreach (var row in results)
        {
            if (!string.Equals(row.Id, targetId, StringComparison.OrdinalIgnoreCase))
                continue;
            return row.Error is null;
        }
        return true;
    }

    public static IReadOnlyList<ServiceEntry> WithOpenUrl(IEnumerable<ServiceEntry> services) =>
        services.Where(s => !string.IsNullOrEmpty(s.OpenUrl)).ToList();
}
