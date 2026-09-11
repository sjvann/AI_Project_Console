using System.Net.Http.Headers;
using System.Text.Json;
using AiProject.Company.Domain;
using Microsoft.Extensions.Configuration;

namespace AiProject.Company.Infrastructure;

public sealed class GitHubDirectory : IGitHubDirectory
{
    readonly HttpClient _http;
    readonly IConfiguration _config;

    public GitHubDirectory(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
        var host = (_config["Company:GitHub:ApiHost"] ?? "https://api.github.com").TrimEnd('/');
        _http.BaseAddress = new Uri(host + "/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiProject-Company-Platform");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = _config["Company:GitHub:Token"] ?? _config["Company:GitHub:Pat"];
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<IReadOnlyList<GitHubMember>> ListOrgMembersAsync(CancellationToken cancellationToken = default)
    {
        var org = _config["Company:GitHub:Org"];
        if (string.IsNullOrWhiteSpace(org))
            return [];
        var json = await _http.GetStringAsync($"orgs/{org}/members", cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e =>
            new GitHubMember(e.GetProperty("login").GetString() ?? "", e.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "")).ToList();
    }

    public async Task<IReadOnlyList<string>> ListReposAsync(CancellationToken cancellationToken = default)
    {
        var org = _config["Company:GitHub:Org"];
        if (string.IsNullOrWhiteSpace(org))
            return [];
        var json = await _http.GetStringAsync($"orgs/{org}/repos?per_page=100", cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e =>
        {
            var full = e.TryGetProperty("full_name", out var n) ? n.GetString() : null;
            return full ?? "";
        }).Where(s => s.Length > 0).ToList();
    }

    public async Task<IReadOnlyList<GitHubIssueRef>> ListOpenIssuesAsync(string ownerRepo, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"repos/{ownerRepo}/issues?state=open&per_page=50", cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var list = new List<GitHubIssueRef>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            if (e.TryGetProperty("pull_request", out _))
                continue;
            var number = e.GetProperty("number").GetInt32();
            var title = e.GetProperty("title").GetString() ?? "";
            string? assignee = null;
            if (e.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object)
                assignee = a.GetProperty("login").GetString();
            var updated = e.GetProperty("updated_at").GetDateTimeOffset();
            var url = e.GetProperty("html_url").GetString() ?? "";
            list.Add(new GitHubIssueRef(ownerRepo, number, title, assignee, updated, url));
        }
        return list;
    }

    public async Task<Outcome> AddAssigneeAsync(string ownerRepo, int issueNumber, string login, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { assignees = new[] { login } });
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"repos/{ownerRepo}/issues/{issueNumber}/assignees", content, cancellationToken);
        if (response.IsSuccessStatusCode)
            return Outcome.Success();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Outcome.Fail("github_writeback", "寫回 GitHub assignee 失敗，已標待同步。");
    }

    public async Task<string?> ResolveLoginAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _http.SendAsync(req, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.TryGetProperty("login", out var login) ? login.GetString() : null;
    }
}
