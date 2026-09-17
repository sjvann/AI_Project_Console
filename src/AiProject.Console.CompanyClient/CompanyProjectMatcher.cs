using AiProject.Company.Contracts;

namespace AiProject.Console.CompanyClient;

public static class CompanyProjectMatcher
{
    public static Guid? Resolve(
        string? githubSlug,
        string? projectName,
        string projectKey,
        IReadOnlyList<AssignmentDto> assignments,
        IReadOnlyDictionary<string, Guid> localMap)
    {
        if (!string.IsNullOrWhiteSpace(projectKey) && localMap.TryGetValue(projectKey, out var mapped) && mapped != Guid.Empty)
            return mapped;
        foreach (var assignment in assignments)
        {
            if (assignment.ProjectId == Guid.Empty)
                continue;
            if (Matches(githubSlug, projectName, projectKey, assignment))
                return assignment.ProjectId;
        }
        return null;
    }

    public static string? MatchLocalName(
        AssignmentDto assignment,
        IEnumerable<(string Key, string Name, string GithubSlug)> localProjects,
        IReadOnlyList<AssignmentDto> assignments,
        IReadOnlyDictionary<string, Guid> localMap)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        foreach (var project in localProjects)
        {
            var id = Resolve(project.GithubSlug, project.Name, project.Key, assignments, localMap);
            if (id == assignment.ProjectId)
                return string.IsNullOrWhiteSpace(project.Name) ? project.Key : project.Name;
        }
        return null;
    }

    static bool Matches(string? githubSlug, string? projectName, string projectKey, AssignmentDto assignment)
    {
        var slug = (githubSlug ?? "").Trim();
        if (!string.IsNullOrEmpty(slug))
        {
            foreach (var repo in assignment.Repos)
            {
                if (string.Equals(repo, slug, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (string.Equals(assignment.ProjectName, slug, StringComparison.OrdinalIgnoreCase))
                return true;
            var leaf = slug.Contains('/') ? slug[(slug.LastIndexOf('/') + 1)..] : slug;
            if (string.Equals(assignment.ProjectName, leaf, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        var name = (projectName ?? "").Trim();
        if (!string.IsNullOrEmpty(name) && string.Equals(assignment.ProjectName, name, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(projectKey) && string.Equals(assignment.ProjectName, projectKey, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
