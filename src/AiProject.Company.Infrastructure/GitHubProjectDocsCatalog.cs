using System.Net.Http.Headers;
using System.Text.Json;
using AiProject.Company.Application;
using AiProject.Company.Domain;
using Microsoft.Extensions.Configuration;

namespace AiProject.Company.Infrastructure;

public sealed class GitHubProjectDocsCatalog : IProjectDocsCatalog
{
    readonly HttpClient _http;
    readonly IConfiguration _config;
    readonly IWorkspaceIntakeReader _intake;
    readonly bool _githubEnabled;

    public GitHubProjectDocsCatalog(HttpClient http, IConfiguration config, IWorkspaceIntakeReader intake)
    {
        _http = http;
        _config = config;
        _intake = intake;
        var host = (_config["Company:GitHub:ApiHost"] ?? "https://api.github.com").TrimEnd('/');
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(host + "/");
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiProject-Company-Platform");
        if (_http.DefaultRequestHeaders.Accept.Count == 0)
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = _config["Company:GitHub:Token"] ?? _config["Company:GitHub:Pat"];
        _githubEnabled = !string.IsNullOrWhiteSpace(token);
        if (_githubEnabled && _http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.Timeout = TimeSpan.FromSeconds(4);
    }

    public async Task<ProjectDocsCatalog> ReadAsync(Project project, CancellationToken ct = default)
    {
        try
        {
            var workspace = string.IsNullOrWhiteSpace(project.WorkspacePath) ? null : project.WorkspacePath.Trim();
            var linked = workspace is not null;
            var docs = new Dictionary<string, ProductDocItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ProjectDocsCatalog.SkeletonNames)
                docs[name] = new ProductDocItem(name, false, null);

            IntakeSummary? intake = null;
            if (linked)
            {
                var dir = Path.Combine(workspace!, "docs", "product");
                foreach (var name in ProjectDocsCatalog.SkeletonNames)
                {
                    var path = Path.Combine(dir, name);
                    if (File.Exists(path))
                        docs[name] = new ProductDocItem(name, true, path);
                }
                intake = await _intake.ReadAsync(workspace!, ct);
            }

            string? pagesUrl = null;
            var pagesPublished = false;
            var firstRepo = project.Repos.FirstOrDefault()?.OwnerRepo;
            if (!string.IsNullOrWhiteSpace(firstRepo) && firstRepo.Contains('/'))
            {
                var parts = firstRepo.Split('/', 2);
                var owner = parts[0];
                var repo = parts[1];
                pagesUrl = PagesUrl(owner, repo);
                if (_githubEnabled)
                {
                    pagesPublished = await PagesPublishedAsync(owner, repo, ct);
                    await MergeGithubProductDocsAsync(owner, repo, docs, ct);
                }
            }

            var list = ProjectDocsCatalog.SkeletonNames.Select(n => docs[n]).ToList();
            string? hint = null;
            if (!linked && project.Repos.Count == 0)
                hint = "先掛倉或連工作區，需求分析在控制台寫、這裡只當入口。";
            else if (!list.Any(d => d.Present) && intake is null && !pagesPublished)
                hint = "規格樹或 GitHub Pages 還沒就緒。請在控制台「建立體系」或發布 Pages。";
            return new ProjectDocsCatalog(linked, workspace, pagesUrl, pagesPublished, list, intake, hint);
        }
        catch
        {
            return ProjectDocsCatalog.Empty("文件目錄暫時讀不到，不影響這一頁其他內容。", !string.IsNullOrWhiteSpace(project.WorkspacePath), project.WorkspacePath);
        }
    }

    string PagesUrl(string owner, string repo)
    {
        var format = _config["Company:GitHub:PagesUrlFormat"];
        if (string.IsNullOrWhiteSpace(format))
        {
            var host = _config["Company:GitHub:PagesHost"] ?? "github.io";
            format = "https://{owner}." + host + "/{repo}/";
        }
        return format.Replace("{owner}", owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{repo}", repo, StringComparison.OrdinalIgnoreCase);
    }

    async Task<bool> PagesPublishedAsync(string owner, string repo, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"repos/{owner}/{repo}/pages", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    async Task MergeGithubProductDocsAsync(string owner, string repo, Dictionary<string, ProductDocItem> docs, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"repos/{owner}/{repo}/contents/docs/product", ct);
            if (!response.IsSuccessStatusCode)
                return;
            using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                return;
            var hrefByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in parsed.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                var html = item.TryGetProperty("html_url", out var h) ? h.GetString() : null;
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                hrefByName[name] = html ?? "";
            }
            foreach (var name in ProjectDocsCatalog.SkeletonNames)
            {
                if (hrefByName.TryGetValue(name, out var href))
                    docs[name] = new ProductDocItem(name, true, string.IsNullOrWhiteSpace(href) ? null : href);
            }
        }
        catch
        {
            // 目錄失敗不擋頁
        }
    }
}
