namespace AiProject.Console.Core.ProcessOps;

/// <summary>
/// 啟動失敗標記。成功啟動／重啟或健康檢查恢復後必須清掉，否則列上會一直顯示「失敗」。
/// </summary>
public static class StartErrorMap
{
    public static void Apply(IDictionary<string, string> errors, string id, string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            errors.Remove(id);
        else
            errors[id] = error;
    }

    public static int ClearHealthy(IDictionary<string, string> errors, IReadOnlyDictionary<string, bool> health)
    {
        var n = 0;
        foreach (var id in errors.Keys.ToList())
        {
            if (health.GetValueOrDefault(id))
            {
                errors.Remove(id);
                n++;
            }
        }
        return n;
    }
}
