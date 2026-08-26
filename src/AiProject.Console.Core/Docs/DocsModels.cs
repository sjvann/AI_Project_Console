namespace AiProject.Console.Core.Docs;

public enum DocsHealth
{
    Missing,
    Incomplete,
    Draft,
    Ready,
}

public enum DocfxDetectKind
{
    Command,
    ProjectManifest,
    HostManifest,
    Missing,
    NoDotnet,
}

public sealed record DocfxDetect(bool Available, DocfxDetectKind Kind)
{
    public string DoctorText => Kind switch
    {
        DocfxDetectKind.Command => "OK",
        DocfxDetectKind.ProjectManifest => "OK（專案 dotnet 本機工具）",
        DocfxDetectKind.HostManifest => "OK（控制台 dotnet 本機工具）",
        DocfxDetectKind.NoDotnet => "缺少（需要 dotnet）",
        _ => "缺少（將用 dotnet tool restore）",
    };

    public string Label => Kind switch
    {
        DocfxDetectKind.Command => "已安裝",
        DocfxDetectKind.ProjectManifest => "已就緒（此專案的 dotnet 本機工具）",
        DocfxDetectKind.HostManifest => "已就緒（控制台倉庫的 dotnet 本機工具）",
        DocfxDetectKind.NoDotnet => "需要先安裝 .NET SDK，才能安裝 DocFX。",
        _ => "此專案還沒有 DocFX 本機工具。",
    };

    public string Badge => Available ? "正常" : Kind == DocfxDetectKind.NoDotnet ? "缺少" : "未安裝";

    public string? HowTo => Kind switch
    {
        DocfxDetectKind.NoDotnet => "先安裝 .NET SDK：https://dot.net/  再回到這裡按「安裝 DocFX」。",
        DocfxDetectKind.Missing =>
            "在專案根目錄執行：\n" +
            "dotnet tool restore\n\n" +
            "若還沒有 .config/dotnet-tools.json，按「安裝 DocFX」會寫入清單並 restore。\n" +
            "之後預覽：dotnet docfx docs/docfx.json --serve",
        _ => null,
    };
}

public sealed record DocsFile(
    string RelPath,
    string Title,
    bool IsStub,
    bool IsConfig);

public sealed record DocsTreeNode(
    string Name,
    string RelPath,
    bool IsFolder,
    DocsFile? File,
    IReadOnlyList<DocsTreeNode> Children);

public sealed record DocsTreeRow(
    int Depth,
    string Name,
    string RelPath,
    bool IsFolder,
    DocsFile? File,
    int ChildCount);

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
    readonly System.Text.StringBuilder _output = new();
    readonly object _gate = new();

    public DocsServeHandle(System.Diagnostics.Process process, string url)
    {
        Process = process;
        Url = url;
        process.OutputDataReceived += (_, e) => Append(e.Data);
        process.ErrorDataReceived += (_, e) => Append(e.Data);
    }

    public System.Diagnostics.Process Process { get; }
    public string Url { get; }
    public string Output
    {
        get
        {
            lock (_gate)
                return _output.ToString().Trim();
        }
    }

    public bool IsRunning
    {
        get
        {
            try { return !Process.HasExited; }
            catch { return false; }
        }
    }

    public bool LooksReady =>
        Output.Contains("Serving", StringComparison.OrdinalIgnoreCase)
        || Output.Contains("http://", StringComparison.OrdinalIgnoreCase);

    void Append(string? line)
    {
        if (line is null)
            return;
        lock (_gate)
            _output.AppendLine(line);
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
