namespace AiProject.Console.Core.Catalog;

public sealed record ServiceStartPlan(
    IReadOnlyList<ServiceEntry> Order,
    IReadOnlySet<string> TargetIds,
    IReadOnlySet<string> OptionalIds,
    IReadOnlyList<string> Errors);

/// <summary>
/// 把 <c>dependsOn</c> 展開成啟動順序：相依在前、循環與未知 id 當錯誤。
/// </summary>
public static class ServiceStartPlanner
{
    public const int DefaultReadyTimeoutMs = 180_000;

    public static string ReadyUrl(ServiceEntry svc) =>
        string.IsNullOrWhiteSpace(svc.Ready) ? svc.Health : svc.Ready;

    public static int ReadyTimeoutMs(ServiceEntry svc) =>
        svc.ReadyTimeoutMs is > 0 ? svc.ReadyTimeoutMs.Value : DefaultReadyTimeoutMs;

    public static ServiceStartPlan ForRunnable(ProjectCatalog catalog, bool skipOptional = false) =>
        ForTargets(catalog, ServiceCatalogBuilder.OrderedRunnable(catalog).Select(s => s.Id), skipOptional);

    public static ServiceStartPlan ForTargets(
        ProjectCatalog catalog,
        IEnumerable<string> targetIds,
        bool skipOptional = false,
        bool skipDepends = false)
    {
        var byId = IndexById(catalog);
        var errors = new List<string>();
        var targets = new List<ServiceEntry>();
        var targetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in targetIds)
        {
            var id = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(id) || !targetSet.Add(id))
                continue;
            if (!byId.TryGetValue(id, out var svc))
            {
                AddError(errors, $"找不到服務：{id}");
                continue;
            }
            if (!string.IsNullOrEmpty(svc.HostedBy))
            {
                AddError(errors, $"「{svc.Label}」隨 {svc.HostedBy} 啟動，請啟動宿主。");
                continue;
            }
            targets.Add(svc);
        }

        if (skipDepends || targets.Count == 0)
        {
            return new ServiceStartPlan(
                targets,
                targetSet,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                errors);
        }

        var nodes = new HashSet<string>(targets.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var edges = new List<(string From, string To)>();
        var hardReach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var optionalReach = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        void Walk(string svcId, bool incomingOptional)
        {
            var cycleAt = stack.FindIndex(x => x.Equals(svcId, StringComparison.OrdinalIgnoreCase));
            if (cycleAt >= 0)
            {
                AddError(errors, "服務相依形成循環：" + string.Join(" → ", stack.Skip(cycleAt).Append(svcId)));
                return;
            }
            if (!byId.TryGetValue(svcId, out var svc))
                return;

            stack.Add(svcId);
            foreach (var dep in svc.Dependencies)
            {
                if (skipOptional && dep.Optional)
                    continue;
                var depId = TryResolveRunnableId(catalog, byId, dep.Id, out var resolveErr);
                if (depId is null)
                {
                    if (!dep.Optional)
                        AddError(errors, resolveErr ?? $"找不到相依服務：{dep.Id}（{svc.Id} 需要）");
                    continue;
                }

                nodes.Add(depId);
                edges.Add((depId, svcId));
                var optionalEdge = incomingOptional || dep.Optional;
                if (optionalEdge)
                    optionalReach.Add(depId);
                else
                    hardReach.Add(depId);
                Walk(depId, optionalEdge);
            }
            stack.RemoveAt(stack.Count - 1);
        }

        foreach (var t in targets)
            Walk(t.Id, incomingOptional: false);

        optionalReach.ExceptWith(hardReach);
        foreach (var t in targets)
            optionalReach.Remove(t.Id);

        var order = TopoSort(catalog, byId, nodes, edges, errors);
        return new ServiceStartPlan(order, targetSet, optionalReach, errors);
    }

    public static string? TryResolveRunnableId(ProjectCatalog catalog, string id, out string? error) =>
        TryResolveRunnableId(catalog, IndexById(catalog), id, out error);

    internal static string? TryResolveRunnableId(
        ProjectCatalog catalog,
        IReadOnlyDictionary<string, ServiceEntry> byId,
        string id,
        out string? error)
    {
        error = null;
        if (!byId.TryGetValue(id, out var svc))
        {
            error = $"找不到相依服務：{id}";
            return null;
        }
        if (string.IsNullOrEmpty(svc.HostedBy))
            return svc.Id;
        var host = ServiceCatalogBuilder.HostService(catalog, svc);
        if (host.Id.Equals(svc.Id, StringComparison.OrdinalIgnoreCase))
        {
            error = $"相依「{id}」的宿主 {svc.HostedBy} 不存在";
            return null;
        }
        return host.Id;
    }

    private static List<ServiceEntry> TopoSort(
        ProjectCatalog catalog,
        IReadOnlyDictionary<string, ServiceEntry> byId,
        HashSet<string> nodes,
        List<(string From, string To)> edges,
        List<string> errors)
    {
        var incoming = nodes.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
        var adj = nodes.ToDictionary(n => n, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in edges)
        {
            if (!nodes.Contains(from) || !nodes.Contains(to))
                continue;
            adj[from].Add(to);
            incoming[to]++;
        }

        var preference = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        foreach (var s in ServiceCatalogBuilder.OrderedRunnable(catalog))
            preference[s.Id] = i++;

        int Rank(string id) => preference.GetValueOrDefault(id, int.MaxValue);

        var zero = incoming.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
        var order = new List<ServiceEntry>();
        while (zero.Count > 0)
        {
            zero.Sort((a, b) =>
            {
                var c = Rank(a).CompareTo(Rank(b));
                return c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });
            var n = zero[0];
            zero.RemoveAt(0);
            if (byId.TryGetValue(n, out var svc))
                order.Add(svc);
            foreach (var m in adj[n])
            {
                incoming[m]--;
                if (incoming[m] == 0)
                    zero.Add(m);
            }
        }

        if (order.Count < nodes.Count)
        {
            var leftover = nodes.Where(n => order.All(s => !s.Id.Equals(n, StringComparison.OrdinalIgnoreCase)));
            AddError(errors, "服務相依形成循環：" + string.Join(" → ", leftover));
        }

        return order;
    }

    private static Dictionary<string, ServiceEntry> IndexById(ProjectCatalog catalog) =>
        catalog.Services.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    private static void AddError(List<string> errors, string message)
    {
        if (!errors.Contains(message, StringComparer.Ordinal))
            errors.Add(message);
    }
}
