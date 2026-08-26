namespace AiProject.Console.Core.GitHub;

/// <summary>
/// 本機倉裡的 GitHub Actions 檔，只做有／無判斷，不產生或代跑 workflow。
/// </summary>
public static class CiWorkflow
{
    public static IReadOnlyList<string> ListFiles(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return [];
        var dir = Path.Combine(Path.GetFullPath(root), ".github", "workflows");
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir)
            .Where(p =>
            {
                var ext = Path.GetExtension(p);
                return ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static bool HasAny(string? root) => ListFiles(root).Count > 0;

    public static bool HasBuildTest(string? root) =>
        ListFiles(root).Any(LooksLikeBuildTest);

    public static string DoctorLine(string? root)
    {
        var files = ListFiles(root);
        if (files.Count == 0)
            return "CI workflow: 無";
        var joined = string.Join("、", files);
        return HasBuildTest(root)
            ? "CI workflow: 有（" + joined + "）"
            : "CI workflow: 僅文件／其他（" + joined + "）";
    }

    public static (string Label, string Badge, bool BuildTest) Describe(string? root)
    {
        var files = ListFiles(root);
        if (files.Count == 0)
            return ("尚未設定", "無", false);
        if (HasBuildTest(root))
            return ("已設定：" + string.Join("、", files), "有", true);
        return ("僅文件／其他：" + string.Join("、", files), "文件", false);
    }

    static bool LooksLikeBuildTest(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem))
            return false;
        if (stem.Equals("docs", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("pages", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("docfx", StringComparison.OrdinalIgnoreCase))
            return false;
        return stem.Equals("ci", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("test", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("build", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("check", StringComparison.OrdinalIgnoreCase);
    }
}
