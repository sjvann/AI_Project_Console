using AiProject.Analysis.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiProject.Analysis.Application;

public sealed class AnalysisCaseCommands
{
    readonly IAnalysisCaseStore _store;
    readonly TimeProvider _clock;
    readonly IAnalysisIssuePublisher _publisher;
    readonly AnalysisGitHubOptions _github;

    public AnalysisCaseCommands(
        IAnalysisCaseStore store,
        IAnalysisIssuePublisher publisher,
        IOptions<AnalysisGitHubOptions>? github = null,
        TimeProvider? clock = null)
    {
        _store = store;
        _publisher = publisher;
        _github = github?.Value ?? new AnalysisGitHubOptions();
        _clock = clock ?? TimeProvider.System;
    }

    public bool GitHubDryRun => _publisher.IsDryRun;
    public string DefaultGitHubRepo => string.IsNullOrWhiteSpace(_github.Repo) ? _publisher.DefaultRepo : _github.Repo;

    public async Task<AnalysisCase> StartAsync(
        string title,
        string conceptText,
        IEnumerable<string>? assumptions = null,
        IEnumerable<string>? openQuestions = null,
        CancellationToken ct = default)
    {
        var created = AnalysisCase.Start(title, conceptText, _clock.GetUtcNow(), assumptions, openQuestions);
        await _store.AddAsync(created, ct);
        return created;
    }

    public Task<AnalysisCase?> GetAsync(Guid id, CancellationToken ct = default) => _store.GetAsync(id, ct);

    public Task<IReadOnlyList<AnalysisCase>> ListAsync(CancellationToken ct = default) => _store.ListAsync(ct);

    public async Task<RequirementsDocument> ProduceRequirementsAsync(
        Guid id,
        IEnumerable<string>? stakeholders = null,
        IEnumerable<string>? inScope = null,
        IEnumerable<string>? outOfScope = null,
        IEnumerable<string>? acceptance = null,
        CancellationToken ct = default)
    {
        var item = await _store.GetAsync(id, ct)
            ?? throw new AnalysisException("not-found", "找不到這則分析案。");
        var doc = item.ProduceRequirements(_clock.GetUtcNow(), stakeholders, inScope, outOfScope, acceptance);
        await _store.UpdateAsync(item, ct);
        return doc;
    }

    public async Task<SpecDocument> ProduceSpecAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(id, ct)
            ?? throw new AnalysisException("not-found", "找不到這則分析案。");
        var doc = item.ProduceSpec(_clock.GetUtcNow());
        await _store.UpdateAsync(item, ct);
        return doc;
    }

    public async Task<IssuePack> PrepareIssuesAsync(Guid id, string? ownerRepo = null, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(id, ct)
            ?? throw new AnalysisException("not-found", "找不到這則分析案。");
        var repo = string.IsNullOrWhiteSpace(ownerRepo) ? DefaultGitHubRepo : ownerRepo!;
        var pack = item.PrepareIssues(repo, _clock.GetUtcNow());
        await _store.UpdateAsync(item, ct);
        return pack;
    }

    public async Task<IssuePack> PublishIssuesAsync(Guid id, string? assignee = null, string? ownerRepo = null, CancellationToken ct = default)
    {
        var item = await _store.GetAsync(id, ct)
            ?? throw new AnalysisException("not-found", "找不到這則分析案。");

        if (item.Issues is null || item.Issues.Drafts.Count == 0)
        {
            var repoForPack = string.IsNullOrWhiteSpace(ownerRepo) ? DefaultGitHubRepo : ownerRepo!;
            item.PrepareIssues(repoForPack, _clock.GetUtcNow());
        }

        var pack = item.Issues
            ?? throw new AnalysisException("need-issues", "請先組出 Issue 包。");
        var repo = string.IsNullOrWhiteSpace(ownerRepo)
            ? (string.IsNullOrWhiteSpace(pack.OwnerRepo) ? DefaultGitHubRepo : pack.OwnerRepo)
            : ownerRepo!;
        if (string.IsNullOrWhiteSpace(repo))
            throw new AnalysisException("required", "請設定 Analysis:GitHub:Repo（owner/repo）。");

        var published = new List<PublishedIssue>();
        foreach (var draft in pack.Drafts)
        {
            var who = draft.Kind is IssueKind.Feature or IssueKind.Debug ? assignee : null;
            published.Add(await _publisher.CreateAsync(repo, draft, who, ct));
        }

        item.MarkIssuesPublished(published, _clock.GetUtcNow());
        await _store.UpdateAsync(item, ct);
        return item.Issues!;
    }
}

public sealed class MemoryAnalysisCaseStore : IAnalysisCaseStore
{
    readonly List<AnalysisCase> _items = [];
    readonly object _gate = new();

    public Task AddAsync(AnalysisCase item, CancellationToken ct = default)
    {
        lock (_gate)
            _items.Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AnalysisCase item, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var idx = _items.FindIndex(x => x.Id == item.Id);
            if (idx < 0)
                throw new AnalysisException("not-found", "找不到這則分析案。");
            _items[idx] = item;
        }
        return Task.CompletedTask;
    }

    public Task<AnalysisCase?> GetAsync(Guid id, CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult(_items.FirstOrDefault(x => x.Id == id));
    }

    public Task<IReadOnlyList<AnalysisCase>> ListAsync(CancellationToken ct = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<AnalysisCase>>(_items.OrderByDescending(x => x.CreatedAt).ToList());
    }
}

public static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddAnalysisApplication(this IServiceCollection services)
    {
        services.AddOptions<AnalysisGitHubOptions>();
        services.AddSingleton<IAnalysisCaseStore, MemoryAnalysisCaseStore>();
        services.AddHttpClient("analysis-github");
        services.AddSingleton<IAnalysisIssuePublisher, AnalysisIssuePublisherRouter>();
        services.AddSingleton<AnalysisCaseCommands>();
        return services;
    }
}
