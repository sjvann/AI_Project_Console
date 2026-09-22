namespace AiProject.Console.Core.Catalog;

public sealed class ServiceGroupNode
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required int Depth { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<ServiceGroupNode> Children { get; init; } = [];
    public IReadOnlyList<ServiceEntry> Services { get; init; } = [];

    public int ServiceCount => Descendants().Count();

    public IEnumerable<ServiceEntry> Descendants()
    {
        foreach (var svc in Services)
            yield return svc;
        foreach (var child in Children)
        {
            foreach (var svc in child.Descendants())
                yield return svc;
        }
    }
}

public static class ServiceGroupTree
{
    public const string DefaultGroup = "其他";

    public static IReadOnlyList<string> SplitPath(string? group)
    {
        var raw = string.IsNullOrWhiteSpace(group) ? DefaultGroup : group.Replace('\\', '/');
        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [DefaultGroup] : parts;
    }

    public static IReadOnlyList<ServiceGroupNode> Build(
        IEnumerable<ServiceEntry> services,
        IReadOnlyDictionary<string, string>? groupDescriptions = null)
    {
        var items = services as IList<ServiceEntry> ?? services.ToList();
        var inferTops = InferableTopGroups(items);
        var roots = new List<MutableNode>();
        var rootMap = new Dictionary<string, MutableNode>(StringComparer.Ordinal);
        foreach (var svc in items)
        {
            var parts = ResolveParts(svc, inferTops);
            MutableNode? parent = null;
            var path = "";
            for (var i = 0; i < parts.Count; i++)
            {
                path = i == 0 ? parts[i] : path + "/" + parts[i];
                var map = parent is null ? rootMap : parent.ChildMap;
                var list = parent is null ? roots : parent.ChildList;
                if (!map.TryGetValue(parts[i], out var node))
                {
                    node = new MutableNode(path, parts[i], i);
                    map[parts[i]] = node;
                    list.Add(node);
                }
                parent = node;
            }
            parent!.Services.Add(svc);
        }
        return roots.Select(n => n.Freeze(groupDescriptions)).ToList();
    }

    internal static string? DescriptionFor(string key, string name, IReadOnlyDictionary<string, string>? descriptions)
    {
        if (descriptions is null || descriptions.Count == 0)
            return null;
        if (descriptions.TryGetValue(key, out var byKey) && !string.IsNullOrWhiteSpace(byKey))
            return byKey.Trim();
        if (descriptions.TryGetValue(name, out var byName) && !string.IsNullOrWhiteSpace(byName))
            return byName.Trim();
        return null;
    }

    internal static IReadOnlyList<string> ResolveParts(ServiceEntry svc, ISet<string> inferTops)
    {
        var parts = SplitPath(svc.Group).ToList();
        if (parts.Count != 1 || !inferTops.Contains(parts[0]))
            return parts;
        var token = InferToken(svc, parts[0]);
        if (token is not null)
            parts.Add(token);
        return parts;
    }

    internal static string? InferToken(ServiceEntry svc, string topGroup)
    {
        var fromLabel = FirstToken(svc.Label);
        if (IsDistinctToken(fromLabel, topGroup))
            return fromLabel;
        var fromProject = ProjectToken(svc.Project, topGroup);
        return IsDistinctToken(fromProject, topGroup) ? fromProject : null;
    }

    private static HashSet<string> InferableTopGroups(IEnumerable<ServiceEntry> services)
    {
        var infer = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in services.GroupBy(s => SplitPath(s.Group)[0], StringComparer.Ordinal))
        {
            var flat = g.Where(s => SplitPath(s.Group).Count == 1).ToList();
            if (flat.Count < 2)
                continue;
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var svc in flat)
            {
                var token = InferToken(svc, g.Key);
                if (token is null)
                    continue;
                counts[token] = counts.GetValueOrDefault(token) + 1;
            }
            if (counts.Count >= 2 && counts.Values.Any(n => n >= 2))
                infer.Add(g.Key);
        }
        return infer;
    }

    private static string? FirstToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var span = text.AsSpan().Trim();
        var i = 0;
        while (i < span.Length && !char.IsWhiteSpace(span[i]) && span[i] is not '/' and not '\\')
            i++;
        var token = i == 0 ? "" : span[..i].ToString().Trim('.', '-', '_');
        return string.IsNullOrEmpty(token) ? null : token;
    }

    private static string? ProjectToken(string? project, string topGroup)
    {
        if (string.IsNullOrWhiteSpace(project))
            return null;
        var segs = project.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0)
            return null;
        var start = segs[0].Equals(topGroup, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (start >= segs.Length)
            return null;
        var folder = segs[start];
        var dot = folder.IndexOf('.');
        return dot > 0 ? folder[..dot] : FirstToken(folder);
    }

    private static bool IsDistinctToken(string? token, string topGroup) =>
        !string.IsNullOrEmpty(token)
        && !token.Equals(topGroup, StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<string> AllKeys(IEnumerable<ServiceGroupNode> roots)
    {
        foreach (var node in Flatten(roots))
            yield return node.Key;
    }

    public static ServiceGroupNode? Find(IEnumerable<ServiceGroupNode> roots, string key)
    {
        foreach (var node in Flatten(roots))
        {
            if (string.Equals(node.Key, key, StringComparison.Ordinal))
                return node;
        }
        return null;
    }

    public static IEnumerable<ServiceGroupNode> Flatten(IEnumerable<ServiceGroupNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    public static IEnumerable<ServiceGroupNode> WalkVisible(
        IEnumerable<ServiceGroupNode> roots,
        Func<string, bool> isCollapsed)
    {
        foreach (var root in roots)
        {
            foreach (var node in WalkVisible(root, isCollapsed))
                yield return node;
        }
    }

    private static IEnumerable<ServiceGroupNode> WalkVisible(
        ServiceGroupNode node,
        Func<string, bool> isCollapsed)
    {
        yield return node;
        if (isCollapsed(node.Key))
            yield break;
        foreach (var child in node.Children)
        {
            foreach (var next in WalkVisible(child, isCollapsed))
                yield return next;
        }
    }

    private sealed class MutableNode(string key, string name, int depth)
    {
        public List<MutableNode> ChildList { get; } = [];
        public Dictionary<string, MutableNode> ChildMap { get; } = new(StringComparer.Ordinal);
        public List<ServiceEntry> Services { get; } = [];

        public ServiceGroupNode Freeze(IReadOnlyDictionary<string, string>? groupDescriptions = null) => new()
        {
            Key = key,
            Name = name,
            Depth = depth,
            Description = DescriptionFor(key, name, groupDescriptions) ?? "",
            Children = ChildList.Select(c => c.Freeze(groupDescriptions)).ToList(),
            Services = Services,
        };
    }
}
