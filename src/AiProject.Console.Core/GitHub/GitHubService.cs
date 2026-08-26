using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed class GithubConfig
{
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Remote { get; set; } = "origin";
    public string DefaultBranch { get; set; } = "main";
    public string Url { get; set; } = "";

    public bool IsComplete() =>
        (!string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo))
        || !string.IsNullOrWhiteSpace(Url);

    public bool LooksGithubHosted()
    {
        if (!string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo))
            return true;
        return (Url ?? "").Contains("github.com", StringComparison.OrdinalIgnoreCase);
    }

    public string Slug() => string.IsNullOrEmpty(Owner) || string.IsNullOrEmpty(Repo) ? "" : $"{Owner}/{Repo}";

    public string WebUrl()
    {
        var slug = Slug();
        return string.IsNullOrEmpty(slug) ? "" : $"https://github.com/{slug}";
    }

    public string RemoteUrl()
    {
        if (!string.IsNullOrWhiteSpace(Url))
            return Url.Trim();
        var slug = Slug();
        return string.IsNullOrEmpty(slug) ? "" : $"https://github.com/{slug}.git";
    }

    public JsonObject AsObject() => new()
    {
        ["owner"] = Owner,
        ["repo"] = Repo,
        ["remote"] = string.IsNullOrEmpty(Remote) ? "origin" : Remote,
        ["defaultBranch"] = string.IsNullOrEmpty(DefaultBranch) ? "main" : DefaultBranch,
        ["url"] = Url,
    };

    public static GithubConfig FromMapping(JsonNode? raw)
    {
        var obj = JsonUtil.Obj(raw);
        if (obj is null)
            return new GithubConfig();
        return new GithubConfig
        {
            Owner = JsonUtil.Str(obj["owner"]),
            Repo = JsonUtil.Str(obj["repo"]),
            Remote = JsonUtil.Pick(JsonUtil.Str(obj["remote"]), "origin"),
            DefaultBranch = JsonUtil.Pick(JsonUtil.Str(obj["defaultBranch"]), JsonUtil.Str(obj["default_branch"]), "main"),
            Url = JsonUtil.Str(obj["url"]),
        };
    }
}

public static class GithubConfigResolver
{
    private static readonly Regex SlugRe = new(@"[:/]([^/:]+)/([^/]+?)(?:\.git)?$", RegexOptions.Compiled);

    public static (string Owner, string Repo) ParseSlug(string remoteUrl)
    {
        var text = (remoteUrl ?? "").Trim().Replace('\\', '/');
        if (string.IsNullOrEmpty(text))
            return ("", "");
        var m = SlugRe.Match(text);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : ("", "");
    }

    public static async Task<GithubConfig> DetectFromGitAsync(string root, string remote = "origin")
    {
        var cfg = new GithubConfig { Remote = string.IsNullOrEmpty(remote) ? "origin" : remote };
        var (code, url) = await CliUtil.RunAsync("git", ["remote", "get-url", cfg.Remote], root, 60_000).ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrEmpty(url))
        {
            cfg.Url = url;
            var (o, r) = ParseSlug(url);
            cfg.Owner = o;
            cfg.Repo = r;
        }
        (code, var branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root, 60_000).ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrEmpty(branch) && branch != "HEAD")
            cfg.DefaultBranch = branch;
        else
        {
            (code, var sym) = await CliUtil.RunAsync("git", ["symbolic-ref", "refs/remotes/origin/HEAD"], root, 60_000).ConfigureAwait(false);
            if (code == 0 && !string.IsNullOrEmpty(sym))
                cfg.DefaultBranch = sym.Split('/')[^1];
        }
        return cfg;
    }

    public static GithubConfig FromManifest(ProjectCatalog? catalog) =>
        catalog is null ? new GithubConfig() : GithubConfig.FromMapping(catalog.Manifest["github"]);

    public static GithubConfig FromLocal(string? root)
    {
        if (root is null)
            return new GithubConfig();
        var data = ConsoleSettingsStore.Load();
        var byRoot = JsonUtil.Obj(data["githubByProject"]);
        return GithubConfig.FromMapping(byRoot?[Path.GetFullPath(root)]);
    }

    public static async Task<GithubConfig> ResolveAsync(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return new GithubConfig();
        var local = FromLocal(catalog.Root);
        var manifest = FromManifest(catalog);
        var detected = await DetectFromGitAsync(catalog.Root, JsonUtil.Pick(local.Remote, manifest.Remote, "origin")).ConfigureAwait(false);
        var remote = JsonUtil.Pick(local.Remote, manifest.Remote, detected.Remote, "origin");
        var owner = JsonUtil.Pick(local.Owner, manifest.Owner, detected.Owner);
        var repo = JsonUtil.Pick(local.Repo, manifest.Repo, detected.Repo);
        var url = JsonUtil.Pick(local.Url, manifest.Url, detected.Url);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            var (o2, r2) = ParseSlug(url);
            owner = JsonUtil.Pick(owner, o2);
            repo = JsonUtil.Pick(repo, r2);
        }
        var branch = JsonUtil.Pick(local.DefaultBranch, manifest.DefaultBranch, detected.DefaultBranch, "main");
        return new GithubConfig
        {
            Owner = owner,
            Repo = repo,
            Remote = remote,
            DefaultBranch = branch,
            Url = url,
        };
    }

    public static void SaveLocal(string root, GithubConfig cfg)
    {
        var data = ConsoleSettingsStore.Load();
        var byRoot = JsonUtil.Obj(data["githubByProject"]) ?? new JsonObject();
        byRoot[Path.GetFullPath(root)] = cfg.AsObject();
        data["githubByProject"] = byRoot;
        ConsoleSettingsStore.Save(data);
    }

    public static string WriteManifest(ProjectCatalog catalog, GithubConfig cfg)
    {
        var path = Path.Combine(catalog.Root, AppInfo.ManifestName);
        var doc = JsonUtil.LoadObject(path);
        doc["github"] = cfg.AsObject();
        if (doc["name"] is null)
            doc["name"] = catalog.Name;
        JsonUtil.SaveObject(path, doc);
        return path;
    }

    public static async Task<string> ApplyRemoteAsync(ProjectCatalog catalog, GithubConfig cfg)
    {
        var remote = string.IsNullOrEmpty(cfg.Remote) ? "origin" : cfg.Remote;
        var url = cfg.RemoteUrl();
        if (string.IsNullOrEmpty(url))
            return "無法套用：缺少 owner/repo 或 url。";
        var (code, existing) = await CliUtil.RunAsync("git", ["remote", "get-url", remote], catalog.Root).ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrEmpty(existing))
        {
            if (existing == url)
                return $"remote `{remote}` 已是 {url}";
            var (c2, err) = await CliUtil.RunAsync("git", ["remote", "set-url", remote, url], catalog.Root).ConfigureAwait(false);
            return c2 == 0 ? $"已更新 `{remote}` → {url}" : $"set-url 失敗：{err}";
        }
        var (c3, err3) = await CliUtil.RunAsync("git", ["remote", "add", remote, url], catalog.Root).ConfigureAwait(false);
        return c3 == 0 ? $"已新增 `{remote}` → {url}" : $"remote add 失敗：{err3}";
    }
}

public readonly record struct CloneResult(string Path, string Message, bool AlreadyExisted);

public sealed record ReleaseItem(
    string Tag,
    string Name,
    bool IsLatest,
    bool IsDraft,
    bool IsPrerelease,
    string PublishedAt);

public sealed class ReleaseInspect
{
    public string LatestGithubTag { get; init; } = "";
    public string LatestGitTag { get; init; } = "";
    public string ProjectVersion { get; init; } = "";
    public string SuggestedTag { get; init; } = "v0.1.0";
    public string CurrentBranch { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool GhOk { get; init; }
}

public sealed record GitChange(string Code, string Path, string? OriginalPath = null)
{
    public string Display()
    {
        var kind = Describe(Code);
        return OriginalPath is null
            ? $"{kind}  {Path}"
            : $"{kind}  {OriginalPath} → {Path}";
    }

    public static string Describe(string code)
    {
        var c = (code ?? "  ").PadRight(2);
        var staged = c[0];
        var work = c[1];
        var mark = work is not ' ' and not '?' ? work : staged;
        return mark switch
        {
            'M' => "修改",
            'A' => "新增",
            'D' => "刪除",
            'R' => "重新命名",
            'C' => "複製",
            'U' => "衝突",
            '?' => "未追蹤",
            '!' => "忽略",
            _ => string.IsNullOrWhiteSpace(code) ? "變更" : code.Trim(),
        };
    }
}

public sealed record ReleaseRequest(
    string Tag,
    string Title = "",
    string Notes = "",
    string Target = "",
    bool Draft = false,
    bool Prerelease = false,
    bool GenerateNotes = true,
    bool MakeLatest = true,
    IReadOnlyList<string>? Assets = null);

public static class GitHubService
{
    public static bool GhAvailable() => CliUtil.CommandExists("gh");

    public static async Task<bool> IsGithubManagedAsync(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return false;
        var cfg = await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        if (cfg.LooksGithubHosted())
            return true;
        if (!await IsGitRepoAsync(catalog.Root).ConfigureAwait(false))
            return false;
        var detected = await GithubConfigResolver.DetectFromGitAsync(catalog.Root).ConfigureAwait(false);
        return detected.LooksGithubHosted();
    }

    private static string CanonicalRemote(string url)
    {
        var (owner, repo) = GithubConfigResolver.ParseSlug(url);
        return string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)
            ? url.Trim().TrimEnd('/').ToLowerInvariant()
            : $"github.com/{owner}/{repo}".ToLowerInvariant();
    }

    private static async Task<bool> SameRemoteAsync(string root, string url)
    {
        var (code, existing) = await CliUtil.RunAsync("git", ["remote", "get-url", "origin"], root).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrEmpty(existing))
            return false;
        return CanonicalRemote(existing) == CanonicalRemote(url);
    }

    public static async Task<bool> IsGitRepoAsync(string root)
    {
        var (code, _) = await CliUtil.RunAsync("git", ["rev-parse", "--is-inside-work-tree"], root).ConfigureAwait(false);
        return code == 0;
    }

    public static async Task<int> DirtyCountAsync(string root)
    {
        var (code, dirty) = await CliUtil.RunAsync("git", ["status", "--porcelain"], root).ConfigureAwait(false);
        if (code != 0)
            return 0;
        return dirty.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static IReadOnlyList<GitChange> ParsePorcelain(string porcelain)
    {
        var list = new List<GitChange>();
        if (string.IsNullOrEmpty(porcelain))
            return list;
        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length < 4)
                continue;
            var code = raw[..2];
            var rest = raw[3..];
            string? orig = null;
            var path = rest;
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0)
            {
                orig = CommitMessageSuggester.UnescapeGitPath(rest[..arrow]);
                path = rest[(arrow + 4)..];
            }
            list.Add(new GitChange(code, CommitMessageSuggester.UnescapeGitPath(path), orig));
        }
        return list;
    }

    public static async Task<IReadOnlyList<GitChange>> ListChangesAsync(string root)
    {
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
            "git", ["-c", "core.quotepath=false", "status", "--porcelain"], root, trim: false).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrEmpty(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrEmpty(err) ? "無法讀取 git 狀態。" : err);
        }
        return ParsePorcelain(stdout);
    }

    public static async Task<string> CommitAsync(string root, string message, bool stageAll = true)
    {
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        var msg = (message ?? "").Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(msg))
            throw new InvalidOperationException("請填寫提交說明。");

        if (stageAll)
        {
            var (addCode, addOut) = await CliUtil.RunAsync("git", ["add", "-A"], root).ConfigureAwait(false);
            if (addCode != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(addOut) ? "git add 失敗。" : addOut);
        }

        var (diffCode, diffOut) = await CliUtil.RunAsync("git", ["diff", "--cached", "--quiet"], root).ConfigureAwait(false);
        if (diffCode == 0)
            throw new InvalidOperationException("沒有可提交的變更（工作區為乾淨，或變更尚未暫存）。");
        if (diffCode != 1)
            throw new InvalidOperationException(string.IsNullOrEmpty(diffOut) ? "無法判斷暫存區狀態。" : diffOut);

        var (code, output) = await CliUtil.RunAsync("git", ["commit", "-m", msg], root).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "git commit 失敗。" : output);

        var (cSha, sha) = await CliUtil.RunAsync("git", ["rev-parse", "--short", "HEAD"], root).ConfigureAwait(false);
        var (cBr, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root).ConfigureAwait(false);
        var lines = new List<string>
        {
            $"已提交到 {(cBr == 0 && !string.IsNullOrEmpty(branch) ? branch : "目前分支")}",
        };
        if (cSha == 0 && !string.IsNullOrEmpty(sha))
            lines.Add(sha);
        if (!string.IsNullOrEmpty(output))
            lines.Add(output);
        return string.Join('\n', lines);
    }

    public static async Task<GitBriefStatus?> TryBriefStatusAsync(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !await IsGitRepoAsync(root).ConfigureAwait(false))
            return null;
        var (c1, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root).ConfigureAwait(false);
        if (c1 != 0 || string.IsNullOrWhiteSpace(branch))
            return null;
        var dirtyN = await DirtyCountAsync(root).ConfigureAwait(false);
        int? ahead = null;
        int? behind = null;
        var (c4, counts) = await CliUtil.RunAsync("git", ["rev-list", "--left-right", "--count", "@{u}...HEAD"], root).ConfigureAwait(false);
        if (c4 == 0 && !string.IsNullOrEmpty(counts))
        {
            var parts = counts.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var b) && int.TryParse(parts[1], out var a))
            {
                behind = b;
                ahead = a;
            }
        }
        return new GitBriefStatus(branch.Trim(), dirtyN, ahead, behind);
    }

    public static async Task<string> GhAuthStatusAsync(string root)
    {
        if (!GhAvailable())
            return "未安裝 gh CLI";
        var (code, output) = await CliUtil.RunAsync("gh", ["auth", "status"], root, 30_000).ConfigureAwait(false);
        return string.IsNullOrEmpty(output) ? (code == 0 ? "已登入" : "未登入（請執行 gh auth login）") : output;
    }

    public static async Task<string> StatusReportAsync(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return "尚未選擇專案。";
        var root = catalog.Root;
        var lines = new List<string> { "GitHub / Git 狀態", "" };
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
        {
            lines.Add($"不是 git 倉庫：{root}");
            return string.Join('\n', lines);
        }
        var cfg = await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var (c1, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root).ConfigureAwait(false);
        branch = c1 == 0 ? branch : "（未知）";
        var (c2, sha) = await CliUtil.RunAsync("git", ["rev-parse", "--short", "HEAD"], root).ConfigureAwait(false);
        sha = c2 == 0 ? sha : "";
        var (c3, dirty) = await CliUtil.RunAsync("git", ["status", "--porcelain"], root).ConfigureAwait(false);
        var dirtyN = c3 == 0 ? dirty.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : -1;
        await CliUtil.RunAsync("git", ["fetch", "--dry-run"], root, 30_000).ConfigureAwait(false);
        var ahead = "?";
        var behind = "?";
        var (c4, counts) = await CliUtil.RunAsync("git", ["rev-list", "--left-right", "--count", "@{u}...HEAD"], root).ConfigureAwait(false);
        if (c4 == 0 && !string.IsNullOrEmpty(counts))
        {
            var parts = counts.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                behind = parts[0];
                ahead = parts[1];
            }
        }
        var workspace = dirtyN == 0 ? "乾淨" : dirtyN >= 0 ? $"{dirtyN} 筆未提交變更" : "未知";
        var gh = (await GhAuthStatusAsync(root).ConfigureAwait(false)).Split('\n')[0];
        lines.AddRange(
        [
            $"路徑：{root}",
            $"分支：{branch}  ({sha})",
            $"工作區：{workspace}",
            $"相對 upstream：領先 {ahead}／落後 {behind}",
            "",
            $"remote：{cfg.Remote}",
            $"倉庫：{(string.IsNullOrEmpty(cfg.Slug()) ? "（未設定）" : cfg.Slug())}",
            $"URL：{(string.IsNullOrEmpty(cfg.RemoteUrl()) ? "（未設定）" : cfg.RemoteUrl())}",
            $"預設分支：{cfg.DefaultBranch}",
            $"設定齊全：{(cfg.IsComplete() ? "是" : "否")}",
            "",
            "gh：" + gh,
        ]);
        var latest = await TryLatestReleaseTagAsync(catalog, cfg).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(latest))
            lines.Add($"最新 Release：{latest}");
        var projectVer = ReleaseVersion.DetectProjectVersion(root);
        if (!string.IsNullOrEmpty(projectVer))
            lines.Add($"專案檔版號：{projectVer}");
        return string.Join('\n', lines);
    }

    public static async Task<string> SyncFromRemoteAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!await IsGitRepoAsync(catalog.Root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var remote = string.IsNullOrEmpty(cfg.Remote) ? "origin" : cfg.Remote;
        var lines = new List<string> { $"同步自 {remote}…" };
        var (code, output) = await CliUtil.RunAsync("git", ["fetch", remote, "--prune"], catalog.Root, 180_000).ConfigureAwait(false);
        lines.Add(string.IsNullOrEmpty(output) ? (code == 0 ? "fetch OK" : "fetch 失敗") : output);
        if (code != 0)
            throw new InvalidOperationException(string.Join('\n', lines));
        (code, var branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], catalog.Root).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrEmpty(branch) || branch == "HEAD")
            throw new InvalidOperationException("無法判斷目前分支（detached HEAD？）。");
        (code, output) = await CliUtil.RunAsync("git", ["pull", "--rebase", remote, branch], catalog.Root, 180_000).ConfigureAwait(false);
        lines.Add(string.IsNullOrEmpty(output) ? (code == 0 ? "pull --rebase OK" : "pull 失敗") : output);
        if (code != 0)
        {
            var (code2, out2) = await CliUtil.RunAsync("git", ["pull", remote, branch], catalog.Root, 180_000).ConfigureAwait(false);
            lines.Add(string.IsNullOrEmpty(out2) ? (code2 == 0 ? "pull OK" : "pull 失敗") : out2);
            if (code2 != 0)
                throw new InvalidOperationException(string.Join('\n', lines));
        }
        return string.Join('\n', lines);
    }

    public static async Task<string> PublishBranchAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!await IsGitRepoAsync(catalog.Root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var remote = string.IsNullOrEmpty(cfg.Remote) ? "origin" : cfg.Remote;
        var (existsCode, _) = await CliUtil.RunAsync("git", ["remote", "get-url", remote], catalog.Root).ConfigureAwait(false);
        if (!cfg.IsComplete() && existsCode != 0)
            throw new InvalidOperationException("尚未設定 GitHub remote。請先完成「GitHub 設定…」。");
        var lines = new List<string>();
        if (cfg.IsComplete())
            lines.Add(await GithubConfigResolver.ApplyRemoteAsync(catalog, cfg).ConfigureAwait(false));
        var (code, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], catalog.Root).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrEmpty(branch) || branch == "HEAD")
            throw new InvalidOperationException("無法判斷目前分支。");
        lines.Add($"發布 {branch} → {remote}…");
        (code, var output) = await CliUtil.RunAsync("git", ["push", "-u", remote, branch], catalog.Root, 300_000).ConfigureAwait(false);
        lines.Add(string.IsNullOrEmpty(output) ? (code == 0 ? "push OK" : "push 失敗") : output);
        if (code != 0)
            throw new InvalidOperationException(string.Join('\n', lines));
        return string.Join('\n', lines);
    }

    public static async Task<bool> OpenOnGithubAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var url = cfg.WebUrl();
        if (string.IsNullOrEmpty(url))
            return false;
        var (code, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], catalog.Root).ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrEmpty(branch) && branch != "HEAD" && branch != cfg.DefaultBranch)
            url = $"{url}/tree/{branch}";
        CliUtil.OpenUrl(url);
        return true;
    }

    public static async Task<string> CreatePullRequestAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string> { "pr", "create" };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        args.AddRange(["--base", string.IsNullOrEmpty(cfg.DefaultBranch) ? "main" : cfg.DefaultBranch, "--fill"]);
        var (code, output) = await CliUtil.RunAsync("gh", args, catalog.Root).ConfigureAwait(false);
        if (code != 0)
        {
            var viewArgs = new List<string> { "pr", "view", "--web" };
            if (!string.IsNullOrEmpty(cfg.Slug()))
                viewArgs.AddRange(["--repo", cfg.Slug()]);
            var (code2, out2) = await CliUtil.RunAsync("gh", viewArgs, catalog.Root, 60_000).ConfigureAwait(false);
            if (code2 == 0)
                return string.IsNullOrEmpty(out2) ? "已開啟既有 PR。" : out2;
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "建立 PR 失敗。" : output);
        }
        return string.IsNullOrEmpty(output) ? "PR 已建立。" : output;
    }

    public static string NormalizeCloneUrl(string spec)
    {
        var text = (spec ?? "").Trim();
        if (string.IsNullOrEmpty(text))
            return "";
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("git@", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            return text;
        text = text.Replace('\\', '/').Trim('/');
        if (text.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var (owner, repo) = GithubConfigResolver.ParseSlug(text);
            return string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)
                ? text
                : $"https://github.com/{owner}/{repo}.git";
        }
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && parts[0].IndexOfAny([':', '@']) < 0
            && parts[1].IndexOfAny([':', '@']) < 0)
        {
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
            return $"https://github.com/{parts[0]}/{repo}.git";
        }
        return "";
    }

    public static string SuggestFolderName(string spec)
    {
        var url = NormalizeCloneUrl(spec);
        var (_, repo) = GithubConfigResolver.ParseSlug(string.IsNullOrEmpty(url) ? spec : url);
        return repo;
    }

    public static async Task<CloneResult> CloneRepositoryAsync(string spec, string destDir, string? branch = null)
    {
        if (!CliUtil.CommandExists("git"))
            throw new InvalidOperationException("找不到 git。請先安裝 Git：https://git-scm.com/");
        var url = NormalizeCloneUrl(spec);
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("請輸入 GitHub URL 或 owner/repo。");
        destDir = Path.GetFullPath(destDir);
        if (Directory.Exists(destDir) && Directory.EnumerateFileSystemEntries(destDir).Any())
        {
            if (await IsGitRepoAsync(destDir).ConfigureAwait(false)
                && await SameRemoteAsync(destDir, url).ConfigureAwait(false))
                return new CloneResult(destDir, $"本機已有此倉庫，直接開啟：{destDir}", true);
            throw new InvalidOperationException($"目錄已存在且非空：{destDir}");
        }

        var parent = Path.GetDirectoryName(destDir);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var args = new List<string> { "clone" };
        if (!string.IsNullOrWhiteSpace(branch))
            args.AddRange(["--branch", branch.Trim(), "--single-branch"]);
        args.Add(url);
        args.Add(destDir);
        var cwd = string.IsNullOrEmpty(parent) ? Environment.CurrentDirectory : parent;
        var (code, output) = await CliUtil.RunAsync("git", args, cwd, 600_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "git clone 失敗。" : output);
        return new CloneResult(destDir, $"已 clone 到 {destDir}", false);
    }

    public static async Task<string> WatchActionsAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 gh CLI。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string> { "run", "list", "--limit", "5" };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        var (code, output) = await CliUtil.RunAsync("gh", args, catalog.Root, 60_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? "無法列出 Actions。" : output);
        return string.IsNullOrEmpty(output) ? "（沒有最近的 workflow runs）" : output;
    }

    public static async Task<ReleaseInspect> InspectReleaseAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var ghOk = GhAvailable();
        var latestGh = "";
        var recentLines = new List<string>();
        if (!ghOk)
            recentLines.Add("未安裝 GitHub CLI（gh），無法讀取或建立 Release。請安裝：https://cli.github.com/");
        else
        {
            try
            {
                var items = await ListReleaseItemsAsync(catalog, cfg, 8).ConfigureAwait(false);
                latestGh = items.FirstOrDefault(r => r.IsLatest)?.Tag
                    ?? items.FirstOrDefault()?.Tag
                    ?? "";
                if (items.Count == 0)
                    recentLines.Add("尚無 GitHub Release。");
                else
                {
                    recentLines.Add("最近發行：");
                    foreach (var item in items)
                        recentLines.Add("  " + FormatReleaseLine(item));
                }
            }
            catch (Exception ex)
            {
                recentLines.Add("無法讀取 GitHub Releases：" + FirstLine(ex.Message));
            }
        }

        var gitTag = await LatestGitTagAsync(catalog.Root).ConfigureAwait(false);
        var projectVer = ReleaseVersion.DetectProjectVersion(catalog.Root);
        var (cBranch, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], catalog.Root).ConfigureAwait(false);
        if (cBranch != 0 || branch == "HEAD")
            branch = cfg.DefaultBranch;
        var suggested = ReleaseVersion.SuggestTag(latestGh, gitTag, projectVer);

        var summary = new List<string>
        {
            $"GitHub 最新：{(string.IsNullOrEmpty(latestGh) ? "（尚無）" : latestGh)}",
            $"git tag：{(string.IsNullOrEmpty(gitTag) ? "（尚無語意化標籤）" : gitTag)}",
            $"專案檔版號：{(string.IsNullOrEmpty(projectVer) ? "（未偵測到）" : projectVer)}",
            $"建議下一版：{suggested}",
            "",
        };
        summary.AddRange(recentLines);

        return new ReleaseInspect
        {
            LatestGithubTag = latestGh,
            LatestGitTag = gitTag,
            ProjectVersion = projectVer,
            SuggestedTag = suggested,
            CurrentBranch = branch,
            Summary = string.Join('\n', summary),
            GhOk = ghOk,
        };
    }

    public static async Task<string> ListReleasesAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var items = await ListReleaseItemsAsync(catalog, cfg, 15).ConfigureAwait(false);
        var lines = new List<string>
        {
            string.IsNullOrEmpty(cfg.Slug()) ? "GitHub Releases" : $"GitHub Releases · {cfg.Slug()}",
            "",
        };
        if (items.Count == 0)
            lines.Add("（尚無 Release）");
        else
        {
            foreach (var item in items)
                lines.Add(FormatReleaseLine(item));
        }
        var web = cfg.WebUrl();
        if (!string.IsNullOrEmpty(web))
        {
            lines.Add("");
            lines.Add(web + "/releases");
        }
        return string.Join('\n', lines);
    }

    public static async Task<bool> OpenReleasesAsync(ProjectCatalog catalog, GithubConfig? cfg = null, string? tag = null)
    {
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var url = cfg.WebUrl();
        if (string.IsNullOrEmpty(url))
            return false;
        url += string.IsNullOrWhiteSpace(tag) ? "/releases" : "/releases/tag/" + tag.Trim();
        CliUtil.OpenUrl(url);
        return true;
    }

    public static async Task<string> CreateReleaseAsync(ProjectCatalog catalog, ReleaseRequest req, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        if (!await IsGitRepoAsync(catalog.Root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        var tag = (req.Tag ?? "").Trim();
        if (!ReleaseVersion.IsValidTag(tag))
            throw new InvalidOperationException("請填寫有效版號／Tag，例如 v1.2.3。");
        if (ReleaseVersion.TryParse(tag, out var parsed))
            tag = string.IsNullOrEmpty(parsed.Prefix) ? "v" + parsed.ToTag() : parsed.ToTag();

        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(req.Title) ? tag : req.Title.Trim();
        var args = new List<string> { "release", "create", tag, "--title", title };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        if (req.GenerateNotes)
            args.Add("--generate-notes");
        if (!string.IsNullOrWhiteSpace(req.Notes))
            args.AddRange(["--notes", req.Notes.Trim()]);
        else if (!req.GenerateNotes)
            args.AddRange(["--notes", title]);
        if (!string.IsNullOrWhiteSpace(req.Target))
            args.AddRange(["--target", req.Target.Trim()]);
        if (req.Draft)
            args.Add("--draft");
        if (req.Prerelease)
            args.Add("--prerelease");
        if (!req.Draft)
            args.Add(req.MakeLatest && !req.Prerelease ? "--latest" : "--latest=false");
        foreach (var asset in req.Assets ?? [])
        {
            if (string.IsNullOrWhiteSpace(asset))
                continue;
            if (!File.Exists(asset))
                throw new InvalidOperationException("找不到附加檔案：" + asset);
            args.Add(asset);
        }

        var (code, output) = await CliUtil.RunAsync("gh", args, catalog.Root, 300_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"建立 Release {tag} 失敗。" : output);
        return string.IsNullOrEmpty(output) ? $"已建立 Release {tag}。" : output;
    }

    private static async Task<IReadOnlyList<ReleaseItem>> ListReleaseItemsAsync(ProjectCatalog catalog, GithubConfig cfg, int limit)
    {
        var args = new List<string>
        {
            "release", "list", "--limit", limit.ToString(),
            "--json", "tagName,name,isLatest,isDraft,isPrerelease,publishedAt",
        };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync("gh", args, catalog.Root, 60_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(stderr) ? (string.IsNullOrEmpty(stdout) ? "無法列出 Releases。" : stdout) : stderr);

        if (string.IsNullOrWhiteSpace(stdout))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<ReleaseItem>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                list.Add(new ReleaseItem(
                    Tag: el.TryGetProperty("tagName", out var tag) ? tag.GetString() ?? "" : "",
                    Name: el.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    IsLatest: el.TryGetProperty("isLatest", out var latest) && latest.ValueKind == JsonValueKind.True,
                    IsDraft: el.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
                    IsPrerelease: el.TryGetProperty("isPrerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
                    PublishedAt: el.TryGetProperty("publishedAt", out var at) ? at.GetString() ?? "" : ""));
            }
            return list;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(string.IsNullOrEmpty(stdout) ? "無法解析 Release 列表。" : stdout);
        }
    }

    private static async Task<string> TryLatestReleaseTagAsync(ProjectCatalog catalog, GithubConfig cfg)
    {
        if (!GhAvailable())
            return "";
        try
        {
            var items = await ListReleaseItemsAsync(catalog, cfg, 1).ConfigureAwait(false);
            return items.FirstOrDefault()?.Tag ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static async Task<string> LatestGitTagAsync(string root)
    {
        var (code, output) = await CliUtil.RunAsync("git", ["tag", "--list", "--sort=-v:refname"], root).ConfigureAwait(false);
        if (code != 0 || string.IsNullOrEmpty(output))
            return "";
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ReleaseVersion.TryParse(line, out _))
                return line;
        }
        return "";
    }

    private static string FormatReleaseLine(ReleaseItem item)
    {
        var flags = new List<string>();
        if (item.IsLatest)
            flags.Add("Latest");
        if (item.IsDraft)
            flags.Add("draft");
        if (item.IsPrerelease)
            flags.Add("pre");
        var mark = flags.Count == 0 ? "" : "  [" + string.Join(", ", flags) + "]";
        var title = string.IsNullOrEmpty(item.Name) || item.Name == item.Tag ? "" : "  " + item.Name;
        var when = FormatPublishedAt(item.PublishedAt);
        return $"{item.Tag}{title}{mark}{(string.IsNullOrEmpty(when) ? "" : "  " + when)}";
    }

    private static string FormatPublishedAt(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return "";
        return DateTimeOffset.TryParse(iso, out var dt) ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm") : iso;
    }

    private static string FirstLine(string text)
    {
        var t = (text ?? "").Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }
}
