namespace AiProject.Company.Domain;

/// <summary>同一發行產物的運營模式（AD-12）。</summary>
public static class HostingModes
{
    public const string SaaS = "SaaS";
    public const string SelfHosted = "SelfHosted";

    public static string Normalize(string? mode)
    {
        if (string.Equals(mode, SaaS, StringComparison.OrdinalIgnoreCase))
            return SaaS;
        if (string.Equals(mode, SelfHosted, StringComparison.OrdinalIgnoreCase))
            return SelfHosted;
        return SelfHosted;
    }

    public static bool IsSaaS(string? mode) => Normalize(mode) == SaaS;
}

/// <summary>主題包 id（DesignSystem data-theme；A8-4）。</summary>
public static class ThemePacks
{
    public const string Default = "pine";

    public static IReadOnlyList<(string Id, string Label)> All { get; } =
    [
        ("pine", "松綠（預設）"),
        ("ocean", "海藍"),
        ("slate", "岩灰"),
        ("clay", "陶土"),
        ("forest", "林蔭"),
    ];

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Default;
        var key = id.Trim().ToLowerInvariant();
        return All.Any(t => t.Id == key) ? key : Default;
    }
}
