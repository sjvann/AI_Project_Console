using System.Text;

namespace AiProject.Console.Core.Docs;

public static class DocsPrompts
{
    public static string FillAll(DocsStatus status, DocsScaffoldContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("請依目前專案狀態，補齊 `docs/` 裡的 Markdown 文件（繁體中文）。");
        sb.AppendLine();
        sb.AppendLine("規定：");
        sb.AppendLine("- 只改 `docs/` 內的 `.md`／`.yml`，不要搬倉根 README。");
        sb.AppendLine("- 保留 YAML front matter 的 title。");
        sb.AppendLine("- 刪掉「（待補）」這類占位句，改寫成可給一般使用者看的完整段落。");
        sb.AppendLine("- 不知就寫「尚未確認」並說明要向誰問，不要捏造。");
        sb.AppendLine("- 不要改程式碼、不要 commit。");
        sb.AppendLine();
        AppendContext(sb, ctx);
        sb.AppendLine("文件地圖：");
        foreach (var f in status.Files.Where(x => !x.IsConfig))
            sb.AppendLine($"- `{f.RelPath}` — {f.Title}" + (f.IsStub ? "（待補）" : ""));
        if (status.MissingScaffold.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("仍缺骨架：");
            foreach (var m in status.MissingScaffold)
                sb.AppendLine("- " + m);
        }
        var stubs = status.Files.Where(f => f.IsStub).Select(f => f.RelPath).ToList();
        if (stubs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("請優先補這些頁：");
            foreach (var s in stubs)
                sb.AppendLine("- " + s);
        }
        return sb.ToString();
    }

    public static string FillOne(string relPath, string content, DocsScaffoldContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"請補齊目前這頁 `docs/{relPath.Replace('\\', '/')}`（繁體中文 Markdown）。");
        sb.AppendLine();
        sb.AppendLine("規定：只改這一檔、保留 front matter、刪掉待補占位、不要捏造、不要改程式碼。");
        sb.AppendLine();
        AppendContext(sb, ctx);
        sb.AppendLine("目前內容：");
        sb.AppendLine("```markdown");
        sb.AppendLine(content ?? "");
        sb.AppendLine("```");
        return sb.ToString();
    }

    static void AppendContext(StringBuilder sb, DocsScaffoldContext ctx)
    {
        sb.AppendLine("專案：" + ctx.Name);
        if (!string.IsNullOrEmpty(ctx.Root))
            sb.AppendLine("根目錄：" + ctx.Root);
        if (!string.IsNullOrEmpty(ctx.GithubSlug))
            sb.AppendLine("GitHub：" + ctx.GithubSlug);
        if (ctx.Services.Count > 0)
        {
            sb.AppendLine("服務：");
            foreach (var s in ctx.Services.Take(20))
                sb.AppendLine("- " + s);
        }
        if (ctx.Projects.Count > 0)
        {
            sb.AppendLine("編譯專案：");
            foreach (var p in ctx.Projects.Take(20))
                sb.AppendLine("- " + p);
        }
        if (!string.IsNullOrWhiteSpace(ctx.ReadmeExcerpt))
        {
            sb.AppendLine();
            sb.AppendLine("倉根 README 摘錄：");
            sb.AppendLine(ctx.ReadmeExcerpt);
            sb.AppendLine();
        }
    }
}
