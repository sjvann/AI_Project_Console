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

public static class GitHubService
{
    public static bool GhAvailable() => CliUtil.CommandExists("gh");

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
}
