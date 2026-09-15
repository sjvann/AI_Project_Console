using AiProject.Analysis.Application;
using AiProject.Analysis.Web.Components;
using AiProject.Shared.Update;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.Configure<AnalysisGitHubOptions>(builder.Configuration.GetSection("Analysis:GitHub"));
builder.Services.AddAnalysisApplication();
builder.Services.AddSingleton(AnalysisProduct.Identity);

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapGet("/health", (UpdateProductIdentity identity) => Results.Ok(new
{
    status = "ok",
    product = "analysis",
    version = identity.CurrentVersion,
})).AllowAnonymous();

app.MapGet("/api/cases/{id:guid}/requirements.md", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Requirements is null) return Results.NotFound();
    return Results.Text(item.Requirements.ToMarkdown(), "text/markdown; charset=utf-8");
});
app.MapGet("/api/cases/{id:guid}/requirements.json", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Requirements is null) return Results.NotFound();
    return Results.Json(item.Requirements.ToJsonShape());
});
app.MapGet("/api/cases/{id:guid}/spec.md", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Spec is null) return Results.NotFound();
    return Results.Text(item.Spec.ToMarkdown(), "text/markdown; charset=utf-8");
});
app.MapGet("/api/cases/{id:guid}/spec.json", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Spec is null) return Results.NotFound();
    return Results.Json(item.Spec.ToJsonShape());
});

app.MapGet("/api/cases/{id:guid}/issues.md", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Issues is null) return Results.NotFound();
    return Results.Text(item.Issues.ToMarkdown(), "text/markdown; charset=utf-8");
});
app.MapGet("/api/cases/{id:guid}/issues.json", async (Guid id, AnalysisCaseCommands cases, CancellationToken ct) =>
{
    var item = await cases.GetAsync(id, ct);
    if (item?.Issues is null) return Results.NotFound();
    return Results.Json(item.Issues.ToJsonShape());
});

app.Run();

public partial class Program;

/// <summary>分析站產品身份（Shared.Update；B1 空站先固定版號）。</summary>
public static class AnalysisProduct
{
    public const string Version = "0.3.0-b3";

    public static UpdateProductIdentity Identity { get; } = new(
        GitHubSlug: "sjvann/AI_Project_Console",
        CurrentVersion: Version,
        ExeName: "AiProject.Analysis.Web",
        UserAgentProduct: "AiProject-Analysis",
        ReleasesUrl: "https://github.com/sjvann/AI_Project_Console/releases",
        LatestReleaseApiUrl: "https://api.github.com/repos/sjvann/AI_Project_Console/releases/latest",
        ReleasesApiUrl: "https://api.github.com/repos/sjvann/AI_Project_Console/releases",
        StagingFolderName: "AI_Project_Analysis_Update",
        DevelopmentMarkerCsproj: "AiProject.Analysis.Web.csproj");
}
