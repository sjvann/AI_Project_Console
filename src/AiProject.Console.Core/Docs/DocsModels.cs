namespace AiProject.Console.Core.Docs;

public enum DocsHealth
{
    Missing,
    Incomplete,
    Draft,
    Ready,
}

public sealed record DocsFile(
    string RelPath,
    string Title,
    bool IsStub,
    bool IsConfig);

public sealed record DocsStatus(
    string Root,
    string DocsRoot,
    DocsHealth Health,
    int FileCount,
    int StubCount,
    bool HasToc,
    bool HasDocfx,
    bool HasWorkflow,
    IReadOnlyList<string> MissingScaffold,
    IReadOnlyList<DocsFile> Files)
{
    public bool IsOk => Health == DocsHealth.Ready;

    public string Label() => Health switch
    {
        DocsHealth.Missing => "無文件",
        DocsHealth.Incomplete => "缺骨架",
        DocsHealth.Draft => $"待補 {StubCount}",
        _ => "就緒",
    };

    public string ChipText() => "文件 " + Label();
}

public sealed record DocsScaffoldContext(
    string Name,
    string Root,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Projects,
    string ReadmeExcerpt,
    string? GithubSlug)
{
    public static DocsScaffoldContext FromCatalog(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return new DocsScaffoldContext("專案", "", [], [], "", null);
        var readme = DocsService.ReadRootReadmeExcerpt(catalog.Root);
        return new DocsScaffoldContext(
            string.IsNullOrWhiteSpace(catalog.Name) ? "專案" : catalog.Name,
            catalog.Root,
            catalog.Services.Select(s => $"{s.Label} ({s.Id})").ToList(),
            catalog.Projects.Select(p => p.Name).ToList(),
            readme,
            null);
    }
}

public sealed record DocsScaffoldResult(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Skipped,
    string Message);

public sealed class DocsServeHandle : IDisposable
{
    public DocsServeHandle(System.Diagnostics.Process process, string url)
    {
        Process = process;
        Url = url;
    }

    public System.Diagnostics.Process Process { get; }
    public string Url { get; }
    public bool IsRunning
    {
        get
        {
            try { return !Process.HasExited; }
            catch { return false; }
        }
    }

    public void Stop()
    {
        try
        {
            if (!Process.HasExited)
                Process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone
        }
    }

    public void Dispose() => Stop();
}
