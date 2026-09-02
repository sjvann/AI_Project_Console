namespace AiProject.Console.Core.ProcessOps;

/// <summary>
/// 服務列過渡狀態。啟動／停止可能要等行程起來或健康檢查，列上要留下「啟動中…」讓使用者知道不是當掉。
/// </summary>
public static class ServiceActivityMap
{
    public const string Starting = "starting";
    public const string Stopping = "stopping";
    public const string Restarting = "restarting";
    public const string Opening = "opening";

    public static string? Label(string? activity) => activity switch
    {
        Starting => "啟動中…",
        Stopping => "停止中…",
        Restarting => "重啟中…",
        Opening => "開啟中…",
        _ => null,
    };

    public static void Set(IDictionary<string, string> map, IEnumerable<string> ids, string activity)
    {
        foreach (var id in ids)
        {
            if (!string.IsNullOrWhiteSpace(id))
                map[id] = activity;
        }
    }

    public static IReadOnlyList<string> IdsWithHosted(
        IReadOnlyList<ServiceEntry> services,
        IEnumerable<ServiceEntry> targets)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var svc in targets)
        {
            var hostId = string.IsNullOrEmpty(svc.HostedBy) ? svc.Id : svc.HostedBy;
            ids.Add(hostId);
        }
        foreach (var svc in services)
        {
            if (svc.HostedBy is not null && ids.Contains(svc.HostedBy))
                ids.Add(svc.Id);
        }
        return ids.ToList();
    }

    public static int ClearOpening(IDictionary<string, string> map)
    {
        var n = 0;
        foreach (var id in map.Keys.ToList())
        {
            if (map[id] != Opening)
                continue;
            map.Remove(id);
            n++;
        }
        return n;
    }

    public static int ClearFailed(IDictionary<string, string> map, IReadOnlyDictionary<string, string> errors)
    {
        var n = 0;
        foreach (var id in errors.Keys.ToList())
        {
            if (map.Remove(id))
                n++;
        }
        return n;
    }

    /// <summary>
    /// 健康檢查已對上預期狀態，或啟動／重啟已失敗時清掉過渡標記。
    /// 重啟要等再度上線才清；不要用「工作結束當下」的舊健康狀態提前清掉。
    /// <paramref name="deadStarted"/>：本控制台寫過 pid、行程已死——桌面程式沒 HTTP 時也要結束「啟動中…」。
    /// </summary>
    public static int Reconcile(
        IDictionary<string, string> map,
        IReadOnlyDictionary<string, bool> health,
        IReadOnlyDictionary<string, string>? errors = null,
        IReadOnlySet<string>? deadStarted = null)
    {
        var n = 0;
        foreach (var id in map.Keys.ToList())
        {
            var activity = map[id];
            if (activity is Starting or Restarting && errors is not null && errors.ContainsKey(id))
            {
                map.Remove(id);
                n++;
                continue;
            }
            var on = health.GetValueOrDefault(id);
            if (activity is Starting or Restarting && !on && deadStarted is not null && deadStarted.Contains(id))
            {
                map.Remove(id);
                n++;
                continue;
            }
            var done = activity switch
            {
                Starting or Restarting => on,
                Stopping => !on,
                Opening => true,
                _ => true,
            };
            if (!done)
                continue;
            map.Remove(id);
            n++;
        }
        return n;
    }
}
