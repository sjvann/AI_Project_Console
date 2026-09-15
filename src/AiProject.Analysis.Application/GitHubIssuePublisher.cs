using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiProject.Analysis.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiProject.Analysis.Application;

public sealed class AnalysisGitHubOptions
{
    /// <summary>目標倉，例如 owner/repo。</summary>
    public string Repo { get; set; } = "";

    public string Token { get; set; } = "";

    public string ApiHost { get; set; } = "https://api.github.com";

    /// <summary>true 或未設 Token 時不打真實 API，改回傳 dry-run URL。</summary>
    public bool DryRun { get; set; } = true;
}

public interface IAnalysisIssuePublisher
{
    bool IsDryRun { get; }
    string DefaultRepo { get; }
    Task<PublishedIssue> CreateAsync(string ownerRepo, IssueDraft draft, string? assignee, CancellationToken ct = default);
}

public sealed class DryRunIssuePublisher : IAnalysisIssuePublisher
{
    public bool IsDryRun => true;
    public string DefaultRepo { get; }

    public DryRunIssuePublisher(string defaultRepo = "") => DefaultRepo = defaultRepo;

    public Task<PublishedIssue> CreateAsync(string ownerRepo, IssueDraft draft, string? assignee, CancellationToken ct = default)
    {
        var repo = string.IsNullOrWhiteSpace(ownerRepo) ? "dry-run/local" : ownerRepo.Trim();
        var fakeNumber = Math.Abs(HashCode.Combine(draft.LocalId, draft.Title)) % 9000 + 1000;
        return Task.FromResult(new PublishedIssue
        {
            LocalId = draft.LocalId,
            Number = fakeNumber,
            HtmlUrl = $"https://github.com/{repo}/issues/{fakeNumber}#dry-run",
            Assignee = string.IsNullOrWhiteSpace(assignee) ? null : assignee.Trim(),
            DryRun = true,
        });
    }
}

public sealed class GitHubIssuePublisher : IAnalysisIssuePublisher
{
    readonly HttpClient _http;
    readonly AnalysisGitHubOptions _opts;

    public GitHubIssuePublisher(HttpClient http, IOptions<AnalysisGitHubOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
        var host = (_opts.ApiHost ?? "https://api.github.com").TrimEnd('/');
        _http.BaseAddress = new Uri(host + "/");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiProject-Analysis");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(_opts.Token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _opts.Token);
    }

    public bool IsDryRun => false;
    public string DefaultRepo => _opts.Repo ?? "";

    public async Task<PublishedIssue> CreateAsync(string ownerRepo, IssueDraft draft, string? assignee, CancellationToken ct = default)
    {
        var repo = ownerRepo.Trim();
        var payload = new Dictionary<string, object?>
        {
            ["title"] = draft.Title,
            ["body"] = draft.Body,
            ["labels"] = draft.Labels.ToArray(),
        };
        if (!string.IsNullOrWhiteSpace(assignee))
            payload["assignees"] = new[] { assignee.Trim() };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"repos/{repo}/issues", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new AnalysisException("github", $"建立 GitHub Issue 失敗（{(int)response.StatusCode}）：{Truncate(body, 240)}");

        using var doc = JsonDocument.Parse(body);
        var number = doc.RootElement.GetProperty("number").GetInt32();
        var url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{repo}/issues/{number}";
        string? assigned = assignee;
        if (doc.RootElement.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object)
            assigned = a.GetProperty("login").GetString() ?? assigned;

        return new PublishedIssue
        {
            LocalId = draft.LocalId,
            Number = number,
            HtmlUrl = url,
            Assignee = string.IsNullOrWhiteSpace(assigned) ? null : assigned,
            DryRun = false,
        };
    }

    static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";
}

public sealed class AnalysisIssuePublisherRouter : IAnalysisIssuePublisher
{
    readonly AnalysisGitHubOptions _opts;
    readonly DryRunIssuePublisher _dry;
    readonly IHttpClientFactory _httpFactory;

    public AnalysisIssuePublisherRouter(IOptions<AnalysisGitHubOptions> opts, IHttpClientFactory httpFactory)
    {
        _opts = opts.Value;
        _httpFactory = httpFactory;
        _dry = new DryRunIssuePublisher(_opts.Repo);
    }

    public bool IsDryRun => _opts.DryRun || string.IsNullOrWhiteSpace(_opts.Token);
    public string DefaultRepo => _opts.Repo ?? "";

    public Task<PublishedIssue> CreateAsync(string ownerRepo, IssueDraft draft, string? assignee, CancellationToken ct = default)
    {
        if (IsDryRun)
            return _dry.CreateAsync(ownerRepo, draft, assignee, ct);

        var http = _httpFactory.CreateClient("analysis-github");
        var publisher = new GitHubIssuePublisher(http, Options.Create(_opts));
        return publisher.CreateAsync(ownerRepo, draft, assignee, ct);
    }
}
