using AiProject.Console.Core.Tech;

namespace AiProject.Console.Core.Build;

/// <summary>
/// 把同一工作區的多個 .NET 目標摺成一次 <c>dotnet build</c> 解法（AD-34）。
/// </summary>
public sealed record BuildGraphPlan(
    IReadOnlyList<string> Steps,
    IReadOnlyDictionary<string, IReadOnlyList<string>> CoveredByStep)
{
    public bool CollapsedToSolution =>
        Steps.Count > 0 && TechStackCatalog.IsSolutionFile(Steps[0]) && CoveredByStep.TryGetValue(Steps[0], out var covered) && covered.Count > 1;
}

public static class BuildGraph
{
    public static BuildGraphPlan Plan(string workspaceRoot, IReadOnlyList<string> targets)
    {
        if (targets.Count == 0)
            return new([], new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase));

        var identity = Identity(targets);
        var sln = TechStackCatalog.FindSolutionFile(workspaceRoot);
        if (string.IsNullOrEmpty(sln))
            return new(targets, identity);

        var covered = new List<string>();
        var rest = new List<string>();
        foreach (var target in targets)
        {
            if (IsDotnetFamily(workspaceRoot, target))
                covered.Add(target);
            else
                rest.Add(target);
        }

        if (covered.Count <= 1)
            return new(targets, identity);

        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [sln] = covered,
        };
        foreach (var extra in rest)
            map[extra] = [extra];

        var steps = new List<string> { sln };
        steps.AddRange(rest);
        return new(steps, map);
    }

    public static bool IsDotnetFamily(string workspaceRoot, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;
        if (TechStackCatalog.IsSolutionFile(target) || TechStackCatalog.IsDotnetProject(target))
            return true;
        var stack = StackCommands.StackIdFor(target);
        if (stack is "dotnet" or "cpp-msbuild")
            return true;
        var plan = StackCommands.PlanBuild(workspaceRoot, target);
        return plan.Display.StartsWith("dotnet build", StringComparison.OrdinalIgnoreCase)
            || plan.MissingToolIds.Contains("dotnet");
    }

    static IReadOnlyDictionary<string, IReadOnlyList<string>> Identity(IReadOnlyList<string> targets)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
            map[t] = [t];
        return map;
    }
}
