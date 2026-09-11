using System.Text;
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
    public string Host { get; set; } = GitHost.PublicHostname;
    public string Kind { get; set; } = GitHost.KindGithub;

    public bool IsComplete() =>
        (!string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo))
        || !string.IsNullOrWhiteSpace(Url);

    public bool LooksGithubHosted()
    {
        if (!GitHost.IssuesReady(ResolvedKind()))
            return false;
        if (!string.IsNullOrWhiteSpace(Url))
            return GitHost.IssuesReady(GitHost.InferKind(Host, Url));
        return !string.IsNullOrWhiteSpace(Owner) && !string.IsNullOrWhiteSpace(Repo);
    }

    public string ResolvedKind() =>
        string.IsNullOrWhiteSpace(Kind) ? GitHost.InferKind(Host, Url) : GitHost.NormalizeKind(Kind);

    public string ResolvedHost()
    {
        if (!string.IsNullOrWhiteSpace(Host))
            return GitHost.Normalize(Host);
        if (!string.IsNullOrWhiteSpace(Url))
            return GitHost.HostFromUrl(Url);
        return GitHost.PublicHostname;
    }

    public string Slug() => string.IsNullOrEmpty(Owner) || string.IsNullOrEmpty(Repo) ? "" : $"{Owner}/{Repo}";

    public string WebUrl()
    {
        var slug = Slug();
        return string.IsNullOrEmpty(slug) ? "" : GitHost.WebUrl(ResolvedHost(), Owner, Repo);
    }

    public string RemoteUrl()
    {
        if (!string.IsNullOrWhiteSpace(Url))
            return Url.Trim();
        var slug = Slug();
        return string.IsNullOrEmpty(slug) ? "" : GitHost.GitUrl(ResolvedHost(), Owner, Repo);
    }

    public JsonObject AsObject() => new()
    {
        ["owner"] = Owner,
        ["repo"] = Repo,
        ["remote"] = string.IsNullOrEmpty(Remote) ? "origin" : Remote,
        ["defaultBranch"] = string.IsNullOrEmpty(DefaultBranch) ? "main" : DefaultBranch,
        ["url"] = Url,
        ["host"] = ResolvedHost(),
        ["kind"] = ResolvedKind(),
    };

    public static GithubConfig FromMapping(JsonNode? raw)
    {
        var obj = JsonUtil.Obj(raw);
        if (obj is null)
            return new GithubConfig();
        var url = JsonUtil.Str(obj["url"]);
        var host = JsonUtil.Pick(JsonUtil.Str(obj["host"]), GitHost.HostFromUrl(url), GitHost.PublicHostname);
        var kind = JsonUtil.Pick(JsonUtil.Str(obj["kind"]), GitHost.InferKind(host, url));
        return new GithubConfig
        {
            Owner = JsonUtil.Str(obj["owner"]),
            Repo = JsonUtil.Str(obj["repo"]),
            Remote = JsonUtil.Pick(JsonUtil.Str(obj["remote"]), "origin"),
            DefaultBranch = JsonUtil.Pick(JsonUtil.Str(obj["defaultBranch"]), JsonUtil.Str(obj["default_branch"]), "main"),
            Url = url,
            Host = GitHost.Normalize(host),
            Kind = GitHost.NormalizeKind(kind),
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
            cfg.Host = GitHost.HostFromUrl(url);
            cfg.Kind = GitHost.InferKind(cfg.Host, url);
        }
        (code, var sym) = await CliUtil.RunAsync("git", ["symbolic-ref", "refs/remotes/origin/HEAD"], root, 60_000).ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrEmpty(sym))
            cfg.DefaultBranch = sym.Split('/')[^1];
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
        var host = JsonUtil.Pick(local.Host, manifest.Host, GitHost.HostFromUrl(url), ConsoleSettingsStore.GetGitHost(), GitHost.PublicHostname);
        var kind = JsonUtil.Pick(local.Kind, manifest.Kind, GitHost.InferKind(host, url));
        return new GithubConfig
        {
            Owner = owner,
            Repo = repo,
            Remote = remote,
            DefaultBranch = branch,
            Url = url,
            Host = GitHost.Normalize(host),
            Kind = GitHost.NormalizeKind(kind),
        };
    }

    public static void SaveLocal(string root, GithubConfig cfg)
    {
        ConsoleSettingsStore.Mutate(data =>
        {
            var byRoot = JsonUtil.Obj(data["githubByProject"]) ?? new JsonObject();
            byRoot[Path.GetFullPath(root)] = cfg.AsObject();
            data["githubByProject"] = byRoot;
        });
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
    string PublishedAt)
{
    public string Title =>
        string.IsNullOrWhiteSpace(Name) || string.Equals(Name.Trim(), Tag, StringComparison.Ordinal)
            ? ""
            : Name.Trim();

    public string PublishedText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PublishedAt))
                return "";
            return DateTimeOffset.TryParse(PublishedAt, out var dt)
                ? dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : PublishedAt;
        }
    }

    public string Line()
    {
        var flags = new List<string>();
        if (IsLatest)
            flags.Add("Latest");
        if (IsDraft)
            flags.Add("draft");
        if (IsPrerelease)
            flags.Add("pre");
        var mark = flags.Count == 0 ? "" : "  [" + string.Join(", ", flags) + "]";
        var title = string.IsNullOrEmpty(Title) ? "" : "  " + Title;
        return $"{Tag}{title}{mark}{(string.IsNullOrEmpty(PublishedText) ? "" : "  " + PublishedText)}";
    }
}

public sealed record ReleaseListView(string Slug, string ReleasesUrl, IReadOnlyList<ReleaseItem> Items)
{
    public string Headline => string.IsNullOrEmpty(Slug) ? "GitHub Releases" : Slug;

    public string ToText()
    {
        var lines = new List<string>
        {
            string.IsNullOrEmpty(Slug) ? "GitHub Releases" : $"GitHub Releases · {Slug}",
            "",
        };
        if (Items.Count == 0)
            lines.Add("（尚無 Release）");
        else
        {
            foreach (var item in Items)
                lines.Add(item.Line());
        }
        if (!string.IsNullOrEmpty(ReleasesUrl))
        {
            lines.Add("");
            lines.Add(ReleasesUrl);
        }
        return string.Join('\n', lines);
    }
}

public sealed class ReleaseInspect
{
    public string LatestGithubTag { get; init; } = "";
    public string LatestGitTag { get; init; } = "";
    public string ProjectVersion { get; init; } = "";
    public string SuggestedTag { get; init; } = "v0.1.0";
    public string CurrentBranch { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool GhOk { get; init; }
    public IReadOnlyList<string> LatestAssetNames { get; init; } = [];
    public bool LatestHasSetup { get; init; }
    public bool Packable { get; init; }
}

public sealed record GitChange(string Code, string Path, string? OriginalPath = null)
{
    public string KindLabel => Describe(Code);

    public string KindTone => KindLabel switch
    {
        "新增" or "未追蹤" or "複製" => "add",
        "刪除" => "del",
        "衝突" => "conflict",
        "重新命名" => "rename",
        _ => "mod",
    };

    public string Display()
    {
        var kind = KindLabel;
        return OriginalPath is null
            ? $"{kind}  {Path}"
            : $"{kind}  {OriginalPath} → {Path}";
    }

    public IReadOnlyList<string> StagePaths()
    {
        if (string.IsNullOrEmpty(OriginalPath))
            return [Path];
        return [OriginalPath, Path];
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
    bool IncludeSource = false,
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
            : $"{GitHost.HostFromUrl(url)}/{owner}/{repo}".ToLowerInvariant();
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
        try
        {
            return (await ListChangesAsync(root).ConfigureAwait(false)).Count;
        }
        catch
        {
            return 0;
        }
    }

    public static IReadOnlyList<GitChange> ParsePorcelain(string porcelain)
    {
        var list = new List<GitChange>();
        if (string.IsNullOrEmpty(porcelain))
            return list;
        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            // porcelain v1：兩欄狀態 + 空白 + 路徑。警告列（warning: …）沒有這個格式。
            if (raw.Length < 4 || raw[2] != ' ')
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

    public static bool LooksLikeIndexLock(string? text)
    {
        var t = text ?? "";
        return t.Contains("index.lock", StringComparison.OrdinalIgnoreCase)
            && (t.Contains("File exists", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Unable to create", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Another git process", StringComparison.OrdinalIgnoreCase));
    }

    public const string IndexLockHint =
        "剛才有另一個 git 動作還在跑（或上次沒清掉鎖檔）。請關閉此視窗後再提交一次。";

    public static bool TryClearStaleIndexLock(string root)
    {
        var path = Path.Combine(root, ".git", "index.lock");
        if (!File.Exists(path))
            return false;
        try
        {
            File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static async Task<(int Code, string Output)> RunGitWriteAsync(string root, params string[] args)
    {
        var (code, output) = await CliUtil.RunAsync("git", args, root).ConfigureAwait(false);
        if (code == 0 || !LooksLikeIndexLock(output))
            return (code, output);
        await Task.Delay(400).ConfigureAwait(false);
        TryClearStaleIndexLock(root);
        (code, output) = await CliUtil.RunAsync("git", args, root).ConfigureAwait(false);
        if (code != 0 && LooksLikeIndexLock(output))
            return (code, IndexLockHint);
        return (code, output);
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
            var (addCode, addOut) = await RunGitWriteAsync(root, "add", "-A").ConfigureAwait(false);
            if (addCode != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(addOut) ? "git add 失敗。" : addOut);
        }

        var (diffCode, diffOut) = await CliUtil.RunAsync("git", ["diff", "--cached", "--quiet"], root).ConfigureAwait(false);
        if (diffCode == 0)
            throw new InvalidOperationException("沒有可提交的變更（工作區為乾淨，或變更尚未暫存）。");
        if (diffCode != 1)
            throw new InvalidOperationException(string.IsNullOrEmpty(diffOut) ? "無法判斷暫存區狀態。" : diffOut);

        var (code, output) = await RunGitWriteAsync(root, "commit", "-m", msg).ConfigureAwait(false);
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

    public static async Task StageSelectedAsync(string root, IReadOnlyList<GitChange> selected)
    {
        if (selected is null || selected.Count == 0)
            throw new InvalidOperationException("請至少選擇一個檔案。");
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");

        var (headCode, _) = await CliUtil.RunAsync("git", ["rev-parse", "--verify", "HEAD"], root).ConfigureAwait(false);
        if (headCode == 0)
        {
            var (resetCode, resetOut) = await RunGitWriteAsync(root, "reset", "-q", "HEAD").ConfigureAwait(false);
            if (resetCode != 0 && !string.IsNullOrWhiteSpace(resetOut))
                throw new InvalidOperationException(resetOut);
        }
        else
        {
            await RunGitWriteAsync(root, "rm", "-r", "--cached", "-f", ".").ConfigureAwait(false);
        }

        foreach (var change in selected)
        {
            foreach (var rel in change.StagePaths())
            {
                var path = (rel ?? "").Trim().Replace('\\', '/');
                if (string.IsNullOrEmpty(path) || path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
                    continue;
                var (addCode, addOut) = await RunGitWriteAsync(root, "add", "-A", "--", path).ConfigureAwait(false);
                if (addCode == 0)
                    continue;
                if (change.KindLabel == "刪除")
                {
                    var (rmCode, rmOut) = await RunGitWriteAsync(root, "rm", "--cached", "--ignore-unmatch", "--", path).ConfigureAwait(false);
                    if (rmCode == 0)
                        continue;
                    throw new InvalidOperationException(string.IsNullOrEmpty(rmOut) ? addOut : rmOut);
                }
                throw new InvalidOperationException(string.IsNullOrEmpty(addOut) ? $"無法暫存 {path}" : addOut);
            }
        }
    }

    public static async Task<string> CommitSelectedAsync(string root, string message, IReadOnlyList<GitChange> selected)
    {
        await StageSelectedAsync(root, selected).ConfigureAwait(false);
        return await CommitAsync(root, message, stageAll: false).ConfigureAwait(false);
    }

    public static async Task<string> PreviewDiffAsync(string root, GitChange change, int maxLines = 200)
    {
        if (change.Code.Contains('?'))
            return PreviewUntracked(root, change, maxLines);

        var args = new List<string> { "-c", "core.quotepath=false", "diff", "HEAD", "-U3", "--" };
        if (!string.IsNullOrEmpty(change.OriginalPath))
            args.Add(change.OriginalPath);
        args.Add(change.Path);
        var (_, stdout, stderr) = await CliUtil.RunCaptureAsync("git", args, root, trim: false).ConfigureAwait(false);
        var text = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        if (string.IsNullOrWhiteSpace(text))
            return $"（{change.KindLabel} {change.Path}：沒有可顯示的 diff，可能是二進位或與 HEAD 相同）";
        return TruncateLines(text.Replace("\r\n", "\n").TrimEnd(), maxLines);
    }

    private static string PreviewUntracked(string root, GitChange change, int maxLines)
    {
        var full = Path.GetFullPath(Path.Combine(root, change.Path.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
            return $"（未追蹤 {change.Path}：路徑無效）";
        if (Directory.Exists(full))
            return $"（未追蹤 {change.Path}：這是資料夾，提交時會一併加入內容）";
        if (!File.Exists(full))
            return $"（未追蹤 {change.Path}：檔案不存在）";
        var info = new FileInfo(full);
        if (info.Length > 256_000)
            return $"（未追蹤 {change.Path}：檔案過大，不預覽）";
        var bytes = File.ReadAllBytes(full);
        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return $"（未追蹤 {change.Path}：二進位檔，不預覽）";
        var lines = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        sb.AppendLine("--- /dev/null");
        sb.AppendLine("+++ b/" + change.Path);
        var n = Math.Min(lines.Length, maxLines);
        for (var i = 0; i < n; i++)
            sb.AppendLine("+" + lines[i]);
        if (lines.Length > maxLines)
            sb.AppendLine($"…（其餘 {lines.Length - maxLines} 行已省略）");
        return sb.ToString().TrimEnd();
    }

    private static string TruncateLines(string text, int maxLines)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= maxLines)
            return text;
        return string.Join('\n', lines.Take(maxLines)) + $"\n…（其餘 {lines.Length - maxLines} 行已省略）";
    }

    public static async Task<string?> CommitPathsIfDirtyAsync(string root, string message, IEnumerable<string> relPaths)
    {
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        var msg = (message ?? "").Replace("\r\n", "\n").Trim();
        if (string.IsNullOrEmpty(msg))
            throw new InvalidOperationException("請填寫提交說明。");
        var rootFull = Path.GetFullPath(root);
        var added = 0;
        foreach (var raw in relPaths)
        {
            var rel = (raw ?? "").Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(rel) || rel.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
                continue;
            var full = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(full) && !Directory.Exists(full))
                continue;
            var (addCode, addOut) = await RunGitWriteAsync(root, "add", "--", rel).ConfigureAwait(false);
            if (addCode != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(addOut) ? "git add 失敗。" : addOut);
            added++;
        }
        if (added == 0)
            return null;
        var (diffCode, diffOut) = await CliUtil.RunAsync("git", ["diff", "--cached", "--quiet"], root).ConfigureAwait(false);
        if (diffCode == 0)
            return null;
        if (diffCode != 1)
            throw new InvalidOperationException(string.IsNullOrEmpty(diffOut) ? "無法判斷暫存區狀態。" : diffOut);
        return await CommitAsync(root, msg, stageAll: false).ConfigureAwait(false);
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
        var hasUpstream = false;
        if (c4 == 0 && !string.IsNullOrEmpty(counts))
        {
            var parts = counts.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var b) && int.TryParse(parts[1], out var a))
            {
                behind = b;
                ahead = a;
                hasUpstream = true;
            }
        }
        return new GitBriefStatus(branch.Trim(), dirtyN, ahead, behind, hasUpstream);
    }

    public static async Task<bool> HasRemoteAsync(string root, string? remote = null)
    {
        if (!string.IsNullOrWhiteSpace(remote))
        {
            var (code, _) = await CliUtil.RunAsync("git", ["remote", "get-url", remote], root).ConfigureAwait(false);
            return code == 0;
        }
        var (c2, names) = await CliUtil.RunAsync("git", ["remote"], root).ConfigureAwait(false);
        return c2 == 0 && names.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length > 0;
    }

    public static bool IsValidBranchName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 200)
            return false;
        if (n is "HEAD" or "." or "..")
            return false;
        if (n.StartsWith('-') || n.StartsWith('/') || n.EndsWith('/') || n.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            return false;
        if (n.Contains("..", StringComparison.Ordinal) || n.Contains("//", StringComparison.Ordinal)
            || n.Contains("@{", StringComparison.Ordinal) || n.Contains('\\'))
            return false;
        foreach (var c in n)
        {
            if (char.IsControl(c) || c is ' ' or '~' or '^' or ':' or '?' or '*' or '[' or ']')
                return false;
        }
        return true;
    }

    public static async Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(string root, bool fetchRemote = true)
    {
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        if (fetchRemote && await HasRemoteAsync(root).ConfigureAwait(false))
            await CliUtil.RunAsync("git", ["fetch", "--prune"], root, 60_000).ConfigureAwait(false);

        var locals = await ReadRefsAsync(root, "refs/heads", remote: false).ConfigureAwait(false);
        var remotes = await ReadRefsAsync(root, "refs/remotes", remote: true).ConfigureAwait(false);
        var localNames = new HashSet<string>(locals.Select(b => b.Name), StringComparer.Ordinal);
        var list = new List<GitBranchInfo>(locals);
        foreach (var remote in remotes)
        {
            var shortName = GitBranchInfo.StripRemotePrefix(remote.Name);
            if (string.IsNullOrEmpty(shortName) || shortName == "HEAD" || localNames.Contains(shortName))
                continue;
            list.Add(remote);
        }
        return list;
    }

    public static async Task<string> SwitchBranchAsync(string root, string name)
    {
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        var target = GitBranchInfo.StripRemotePrefix((name ?? "").Trim());
        if (!IsValidBranchName(target))
            throw new InvalidOperationException("分支名稱無效。");
        var dirty = await DirtyCountAsync(root).ConfigureAwait(false);
        if (dirty > 0)
            throw new InvalidOperationException($"工作區有 {dirty} 筆未提交變更。請先提交或還原，才能切換分支。");

        var (cCur, current) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root).ConfigureAwait(false);
        if (cCur == 0 && string.Equals(current, target, StringComparison.Ordinal))
            return $"已在 {target}。";

        var (code, output) = await CliUtil.RunAsync("git", ["switch", "--guess", target], root).ConfigureAwait(false);
        if (code != 0)
        {
            var (code2, out2) = await CliUtil.RunAsync("git", ["checkout", target], root).ConfigureAwait(false);
            if (code2 != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(output) ? (string.IsNullOrEmpty(out2) ? $"無法切換到 {target}。" : out2) : output);
        }
        return $"已切換到 {target}";
    }

    public static async Task<string> CreateBranchAsync(string root, string name)
    {
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
            throw new InvalidOperationException("不是 git 倉庫。");
        var target = (name ?? "").Trim();
        if (!IsValidBranchName(target))
            throw new InvalidOperationException("請填寫有效的分支名稱，例如 feat/login。");
        var dirty = await DirtyCountAsync(root).ConfigureAwait(false);
        if (dirty > 0)
            throw new InvalidOperationException($"工作區有 {dirty} 筆未提交變更。請先提交或還原，才能建立並切換分支。");

        var (code, output) = await CliUtil.RunAsync("git", ["switch", "-c", target], root).ConfigureAwait(false);
        if (code != 0)
        {
            var (code2, out2) = await CliUtil.RunAsync("git", ["checkout", "-b", target], root).ConfigureAwait(false);
            if (code2 != 0)
                throw new InvalidOperationException(string.IsNullOrEmpty(output) ? (string.IsNullOrEmpty(out2) ? $"無法建立分支 {target}。" : out2) : output);
        }
        return $"已建立並切換到 {target}";
    }

    private static async Task<List<GitBranchInfo>> ReadRefsAsync(string root, string pattern, bool remote)
    {
        var format = remote
            ? "%(refname:short)\t%(objectname:short)"
            : "%(refname:short)\t%(objectname:short)\t%(HEAD)\t%(upstream:short)";
        var (code, output) = await CliUtil.RunAsync("git", ["for-each-ref", $"--format={format}", pattern], root).ConfigureAwait(false);
        var list = new List<GitBranchInfo>();
        if (code != 0 || string.IsNullOrWhiteSpace(output))
            return list;
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cols = raw.Split('\t');
            if (cols.Length < 2 || string.IsNullOrWhiteSpace(cols[0]))
                continue;
            var name = cols[0].Trim();
            if (remote && (name.EndsWith("/HEAD", StringComparison.Ordinal) || name.Equals("HEAD", StringComparison.Ordinal)))
                continue;
            var sha = cols[1].Trim();
            var isCurrent = !remote && cols.Length > 2 && cols[2].Trim() == "*";
            var tracking = !remote && cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) ? cols[3].Trim() : null;
            list.Add(new GitBranchInfo(name, isCurrent, remote, tracking, sha));
        }
        return list;
    }

    public static async Task<string> GhAuthStatusAsync(string root, string? host = null)
    {
        if (!GhAvailable())
            return "未安裝 gh CLI";
        var cfg = new GithubConfig { Host = GitHost.Normalize(host) };
        var (code, output) = await GhCli.RunAsync(["auth", "status"], root, cfg, 30_000).ConfigureAwait(false);
        return string.IsNullOrEmpty(output) ? (code == 0 ? "已登入" : "未登入（請執行 gh auth login）") : output;
    }

    public static async Task<string> StatusReportAsync(ProjectCatalog? catalog) =>
        (await StatusViewAsync(catalog).ConfigureAwait(false)).Text;

    public static async Task<InfoReport> StatusViewAsync(ProjectCatalog? catalog)
    {
        if (catalog is null)
        {
            return new InfoReport(
                Title: "GitHub 狀態",
                Hint: "選擇專案後可看分支、工作區與遠端設定。",
                Headline: "尚未選擇專案",
                HeadlineDetail: "請先選擇專案目錄。",
                Tone: "info",
                Sections: [],
                Text: "GitHub / Git 狀態\n\n尚未選擇專案。");
        }

        var root = catalog.Root;
        var lines = new List<string> { "GitHub / Git 狀態", "" };
        if (!await IsGitRepoAsync(root).ConfigureAwait(false))
        {
            lines.Add($"不是 git 倉庫：{root}");
            return new InfoReport(
                Title: "GitHub 狀態",
                Hint: "這個目錄還不是 git 倉庫，無法讀遠端與分支。",
                Headline: "不是 git 倉庫",
                HeadlineDetail: root,
                Tone: "warn",
                Sections:
                [
                    new InfoSection(
                        "workspace",
                        "工作區",
                        [new InfoField("路徑", root, Badge: "不是 git", Tone: "warn")]),
                ],
                Text: string.Join('\n', lines));
        }

        var cfg = await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var (c1, branch) = await CliUtil.RunAsync("git", ["rev-parse", "--abbrev-ref", "HEAD"], root).ConfigureAwait(false);
        branch = c1 == 0 ? branch : "（未知）";
        var (c2, sha) = await CliUtil.RunAsync("git", ["rev-parse", "--short", "HEAD"], root).ConfigureAwait(false);
        sha = c2 == 0 ? sha : "";
        var dirtyN = await DirtyCountAsync(root).ConfigureAwait(false);
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
        var workspaceTone = dirtyN == 0 ? "ok" : dirtyN > 0 ? "warn" : "info";
        var gh = (await GhAuthStatusAsync(root, cfg.ResolvedHost()).ConfigureAwait(false)).Split('\n')[0];
        var slug = cfg.Slug();
        var remoteUrl = cfg.RemoteUrl();
        var complete = cfg.IsComplete();
        var latest = await TryLatestReleaseTagAsync(catalog, cfg).ConfigureAwait(false);
        var projectVer = ReleaseVersion.DetectProjectVersion(root);

        lines.AddRange(
        [
            $"路徑：{root}",
            $"分支：{branch}  ({sha})",
            $"工作區：{workspace}",
            $"相對 upstream：領先 {ahead}／落後 {behind}",
            "",
            $"remote：{cfg.Remote}",
            $"倉庫：{(string.IsNullOrEmpty(slug) ? "（未設定）" : slug)}",
            $"URL：{(string.IsNullOrEmpty(remoteUrl) ? "（未設定）" : remoteUrl)}",
            $"預設分支：{cfg.DefaultBranch}",
            $"設定齊全：{(complete ? "是" : "否")}",
            "",
            "gh：" + gh,
        ]);
        if (!string.IsNullOrEmpty(latest))
            lines.Add($"最新 Release：{latest}");
        if (!string.IsNullOrEmpty(projectVer))
            lines.Add($"專案檔版號：{projectVer}");

        var sections = new List<InfoSection>
        {
            new(
                "workspace",
                "工作區",
                [
                    new InfoField("路徑", root),
                    new InfoField("分支", branch, Detail: string.IsNullOrEmpty(sha) ? null : sha),
                    new InfoField("工作區", workspace, Badge: workspace, Tone: workspaceTone),
                ]),
            new(
                "remote",
                "遠端",
                [
                    new InfoField("相對 upstream", $"領先 {ahead}／落後 {behind}"),
                    new InfoField("remote", string.IsNullOrEmpty(cfg.Remote) ? "origin" : cfg.Remote),
                    new InfoField("倉庫", string.IsNullOrEmpty(slug) ? "（未設定）" : slug, Badge: complete ? "齊全" : "未齊全", Tone: complete ? "ok" : "warn"),
                    new InfoField("URL", string.IsNullOrEmpty(remoteUrl) ? "（未設定）" : remoteUrl),
                    new InfoField("預設分支", string.IsNullOrEmpty(cfg.DefaultBranch) ? "main" : cfg.DefaultBranch),
                ],
                Badge: complete ? "齊全" : "未齊全",
                Tone: complete ? "ok" : "warn"),
            new(
                "version",
                "版本與登入",
                [
                    new InfoField("gh", gh),
                    new InfoField("最新 Release", string.IsNullOrEmpty(latest) ? "（尚無）" : latest),
                    new InfoField("專案檔版號", string.IsNullOrEmpty(projectVer) ? "（未偵測）" : projectVer),
                ]),
        };

        var tone = !complete || dirtyN > 0 ? "warn" : "ok";
        var headline = string.IsNullOrEmpty(slug) ? branch : $"{slug} · {branch}";
        var detail = dirtyN == 0
            ? $"工作區乾淨 · 領先 {ahead}／落後 {behind}"
            : $"{workspace} · 領先 {ahead}／落後 {behind}";

        return new InfoReport(
            Title: "GitHub 狀態",
            Hint: "控制台只讀目前倉庫狀態。提交、同步與發行請用 Pulse 或 GitHub 操作台。",
            Headline: headline,
            HeadlineDetail: detail,
            Tone: tone,
            Sections: sections,
            Text: string.Join('\n', lines),
            PrimaryAction: string.IsNullOrEmpty(cfg.WebUrl()) ? null : "open-github",
            PrimaryLabel: string.IsNullOrEmpty(cfg.WebUrl()) ? null : "在 GitHub 開啟");
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

    public static string PullRequestClosesBody(int issueNumber) =>
        issueNumber > 0 ? "Closes #" + issueNumber : "";

    public static IReadOnlyList<string> BuildCreatePrArgs(GithubConfig cfg, int? closesIssue = null)
    {
        var args = new List<string> { "pr", "create" };
        args.AddRange(["--base", string.IsNullOrEmpty(cfg.DefaultBranch) ? "main" : cfg.DefaultBranch, "--fill"]);
        var closes = closesIssue is > 0 ? PullRequestClosesBody(closesIssue.Value) : "";
        if (!string.IsNullOrEmpty(closes))
            args.AddRange(["--body", closes]);
        return args;
    }

    public static async Task<string> CreatePullRequestAsync(
        ProjectCatalog catalog,
        GithubConfig? cfg = null,
        int? closesIssue = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var brief = await TryBriefStatusAsync(catalog.Root).ConfigureAwait(false);
        var block = IssueCompletion.CreatePrBlockReason(brief, cfg.DefaultBranch, hasPr: false);
        if (!string.IsNullOrEmpty(block))
            throw new InvalidOperationException(block);
        var args = BuildCreatePrArgs(cfg, closesIssue);
        var (code, output) = await GhCli.RunAsync(args, catalog.Root, cfg).ConfigureAwait(false);
        if (code != 0)
        {
            var viewArgs = new List<string> { "pr", "view", "--web" };
            var (code2, out2) = await GhCli.RunAsync(viewArgs, catalog.Root, cfg, 60_000).ConfigureAwait(false);
            if (code2 == 0)
                return string.IsNullOrEmpty(out2) ? "已開啟既有 PR。" : out2;
            throw new InvalidOperationException(IssueCompletion.ExplainCreatePrFailure(output));
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
        if (text.Contains('.', StringComparison.Ordinal) && text.Contains('/', StringComparison.Ordinal)
            && !text.Contains("://", StringComparison.Ordinal)
            && !text.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            var (owner, repo) = GithubConfigResolver.ParseSlug(text);
            var host = GitHost.HostFromUrl(text);
            return string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo)
                ? text
                : GitHost.GitUrl(host, owner, repo);
        }
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && parts[0].IndexOfAny([':', '@']) < 0
            && parts[1].IndexOfAny([':', '@']) < 0)
        {
            var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
            return GitHost.GitUrl(ConsoleSettingsStore.GetGitHost(), parts[0], repo);
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
        var snap = await GetActionsSnapshotAsync(catalog, cfg).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(snap.Error))
            throw new InvalidOperationException(snap.Error);
        return snap.FormatReport();
    }

    public static async Task<ActionsSnapshot> GetActionsSnapshotAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        var hasLocal = CiWorkflow.HasAny(catalog.Root);
        if (!GhAvailable())
            return ActionsSnapshot.Unavailable("需要 GitHub CLI（gh）才能讀 Actions。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string>
        {
            "run", "list", "--limit", "8",
            "--json", "databaseId,name,displayTitle,status,conclusion,headBranch,event,url,updatedAt,createdAt",
        };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        var (code, output) = await GhCli.RunAsync(args, catalog.Root, cfg, 60_000).ConfigureAwait(false);
        if (code != 0)
            return ActionsSnapshot.Unavailable(string.IsNullOrEmpty(output) ? "無法列出 Actions。" : FirstLine(output));
        var runs = ActionsStatus.ParseRuns(output);
        return new ActionsSnapshot(runs, hasLocal || runs.Count > 0);
    }

    public static bool OpenWorkflowRun(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        CliUtil.OpenUrl(url.Trim());
        return true;
    }

    public static async Task<PullRequestStatus> GetPullRequestStatusAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            return PullRequestStatus.Unavailable("需要 GitHub CLI（gh）才能讀 PR。");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string>
        {
            "pr", "view",
            "--json", "title,url,state,isDraft,headRefName,baseRefName,reviewDecision,statusCheckRollup",
        };
        var (code, output) = await GhCli.RunAsync(args, catalog.Root, cfg, 60_000).ConfigureAwait(false);
        if (code != 0)
            return PrStatus.LooksLikeNoPr(output)
                ? PullRequestStatus.None()
                : PullRequestStatus.Unavailable(string.IsNullOrEmpty(output) ? "無法讀取 PR。" : FirstLine(output));
        try
        {
            var parsed = PrStatus.Parse(output);
            return parsed.HasPr ? parsed : PullRequestStatus.None();
        }
        catch (Exception ex)
        {
            return PullRequestStatus.Unavailable(FirstLine(ex.Message));
        }
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
        var packable = ConsoleReleasePack.LooksPackable(catalog.Root);
        IReadOnlyList<string> latestAssets = [];
        if (ghOk && !string.IsNullOrEmpty(latestGh))
        {
            try
            {
                latestAssets = await ListReleaseAssetNamesAsync(catalog, cfg, latestGh).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                recentLines.Add("無法讀取最新 Release 資產：" + FirstLine(ex.Message));
            }
        }
        var latestHasSetup = ConsoleReleasePack.HasSetupAsset(latestAssets);

        var summary = new List<string>
        {
            $"GitHub 最新：{(string.IsNullOrEmpty(latestGh) ? "（尚無）" : latestGh)}",
            $"git tag：{(string.IsNullOrEmpty(gitTag) ? "（尚無語意化標籤）" : gitTag)}",
            $"專案檔版號：{(string.IsNullOrEmpty(projectVer) ? "（未偵測到）" : projectVer)}",
            $"建議下一版：{suggested}",
            "",
        };
        if (packable)
        {
            if (string.IsNullOrEmpty(latestGh))
                summary.Add("自動更新需要附加 *-win-x64-setup.exe。正式發行時會先打包再上傳。");
            else if (!latestHasSetup)
                summary.Add($"最新 {latestGh} 沒有 *-win-x64-setup.exe。已安裝使用者的自動更新會改開 GitHub 頁。發行時會打包並補上。");
            else
                summary.Add($"最新 {latestGh} 已附安裝程式。");
            summary.Add("");
        }
        summary.AddRange(recentLines);
        if (latestAssets.Count > 0)
        {
            summary.Add("");
            summary.Add("最新資產：");
            foreach (var name in latestAssets)
                summary.Add("  " + name);
        }

        return new ReleaseInspect
        {
            LatestGithubTag = latestGh,
            LatestGitTag = gitTag,
            ProjectVersion = projectVer,
            SuggestedTag = suggested,
            CurrentBranch = branch,
            Summary = string.Join('\n', summary),
            GhOk = ghOk,
            LatestAssetNames = latestAssets,
            LatestHasSetup = latestHasSetup,
            Packable = packable,
        };
    }

    public static async Task<string> ListReleasesAsync(ProjectCatalog catalog, GithubConfig? cfg = null) =>
        (await LoadReleaseListAsync(catalog, cfg).ConfigureAwait(false)).ToText();

    public static async Task<ReleaseListView> LoadReleaseListAsync(ProjectCatalog catalog, GithubConfig? cfg = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var items = await ListReleaseItemsAsync(catalog, cfg, 15).ConfigureAwait(false);
        var web = cfg.WebUrl();
        return new ReleaseListView(
            Slug: cfg.Slug(),
            ReleasesUrl: string.IsNullOrEmpty(web) ? "" : web + "/releases",
            Items: items);
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

    public static async Task<string> CreateReleaseAsync(
        ProjectCatalog catalog,
        ReleaseRequest req,
        GithubConfig? cfg = null,
        IProgress<string>? progress = null)
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

        var assets = await PrepareReleaseAssetsAsync(catalog, req, cfg, progress).ConfigureAwait(false);
        foreach (var asset in assets)
        {
            if (!File.Exists(asset))
                throw new InvalidOperationException("找不到附加檔案：" + asset);
            args.Add(asset);
        }

        var hasAssets = assets.Count > 0;
        progress?.Report(ReleaseRunState.StageLine(
            "upload",
            hasAssets ? "正在建立 GitHub Release 並上傳安裝包…" : "正在建立 GitHub Release…"));
        var timeout = hasAssets ? 600_000 : 300_000;
        var (code, output) = await GhCli.RunAsync(
            args,
            catalog.Root,
            cfg,
            timeout,
            onLine: progress is null ? null : line => ForwardGhLine(progress, line)).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"建立 Release {tag} 失敗。" : output);
        return string.IsNullOrEmpty(output) ? $"已建立 Release {tag}。" : output;
    }

    public static async Task<IReadOnlyList<string>> PrepareReleaseAssetsAsync(
        ProjectCatalog catalog,
        ReleaseRequest req,
        GithubConfig? cfg = null,
        IProgress<string>? progress = null)
    {
        var assets = ReleaseSource.SelectUploadAssets(req.Assets, req.IncludeSource).ToList();
        if (!req.IncludeSource || assets.Any(ReleaseSource.IsSourceAsset))
            return assets;

        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var tag = (req.Tag ?? "").Trim();
        if (ReleaseVersion.TryParse(tag, out var parsed))
            tag = string.IsNullOrEmpty(parsed.Prefix) ? "v" + parsed.ToTag() : parsed.ToTag();
        var repo = string.IsNullOrWhiteSpace(cfg.Repo)
            ? Path.GetFileName(Path.GetFullPath(catalog.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : cfg.Repo;
        var dest = Path.Combine(Path.GetTempPath(), "ai-project-release", ReleaseSource.FileName(repo, tag));
        var committish = string.IsNullOrWhiteSpace(req.Target) ? "HEAD" : req.Target.Trim();
        assets.Add(await ReleaseSource.ArchiveAsync(catalog.Root, tag, dest, committish, progress).ConfigureAwait(false));
        return assets;
    }

    public static async Task<string> PublishReleaseAsync(
        ProjectCatalog catalog,
        ReleaseRequest req,
        GithubConfig? cfg = null,
        IProgress<string>? progress = null)
    {
        try
        {
            return await CreateReleaseAsync(catalog, req, cfg, progress).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (
            LooksLikeReleaseExists(ex.Message)
            && (req.IncludeSource || req.Assets is { Count: > 0 }))
        {
            var assets = await PrepareReleaseAssetsAsync(catalog, req, cfg, progress).ConfigureAwait(false);
            if (assets.Count == 0)
                throw;
            progress?.Report(ReleaseRunState.StageLine("upload", "Release 已存在，改為補上傳安裝包…"));
            var uploaded = await UploadReleaseAssetsAsync(catalog, req.Tag, assets, cfg, progress).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(uploaded)
                ? $"Release {req.Tag.Trim()} 已存在，已補上安裝包。"
                : uploaded;
        }
    }

    public static async Task<string> UploadReleaseAssetsAsync(
        ProjectCatalog catalog,
        string tag,
        IReadOnlyList<string> assets,
        GithubConfig? cfg = null,
        IProgress<string>? progress = null)
    {
        if (!GhAvailable())
            throw new InvalidOperationException("需要 GitHub CLI（gh）。請安裝：https://cli.github.com/");
        var name = (tag ?? "").Trim();
        if (!ReleaseVersion.IsValidTag(name))
            throw new InvalidOperationException("請填寫有效版號／Tag，例如 v1.2.3。");
        if (ReleaseVersion.TryParse(name, out var parsed))
            name = string.IsNullOrEmpty(parsed.Prefix) ? "v" + parsed.ToTag() : parsed.ToTag();
        cfg ??= await GithubConfigResolver.ResolveAsync(catalog).ConfigureAwait(false);
        var args = new List<string> { "release", "upload", name };
        GhCli.AddRepo(args, cfg);
        args.Add("--clobber");
        var any = false;
        foreach (var asset in assets)
        {
            if (string.IsNullOrWhiteSpace(asset))
                continue;
            if (!File.Exists(asset))
                throw new InvalidOperationException("找不到附加檔案：" + asset);
            args.Add(asset);
            any = true;
        }
        if (!any)
            throw new InvalidOperationException("沒有可上傳的安裝包。");
        progress?.Report(ReleaseRunState.StageLine("upload", "正在上傳安裝包到 GitHub…"));
        var (code, output) = await GhCli.RunAsync(
            args,
            catalog.Root,
            cfg,
            600_000,
            onLine: progress is null ? null : line => ForwardGhLine(progress, line)).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(output) ? $"上傳 Release {name} 資產失敗。" : output);
        return string.IsNullOrEmpty(output) ? $"已補上 Release {name} 的安裝包。" : output;
    }

    static void ForwardGhLine(IProgress<string> progress, string line)
    {
        var user = ConsoleReleasePack.InterpretGhLine(line);
        if (!string.IsNullOrEmpty(user))
            progress.Report(user);
    }

    public static bool LooksLikeReleaseExists(string? text)
    {
        var t = text ?? "";
        return t.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || t.Contains("already_exists", StringComparison.OrdinalIgnoreCase)
            || t.Contains("HTTP 422", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<IReadOnlyList<string>> ListReleaseAssetNamesAsync(
        ProjectCatalog catalog,
        GithubConfig cfg,
        string tag)
    {
        var name = (tag ?? "").Trim();
        if (string.IsNullOrEmpty(name))
            return [];
        var args = new List<string> { "release", "view", name, "--json", "assets" };
        GhCli.AddRepo(args, cfg);
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, catalog.Root, cfg, 60_000).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrEmpty(stderr) ? (string.IsNullOrEmpty(stdout) ? "無法讀取 Release 資產。" : stdout) : stderr);
        return ParseReleaseAssetNames(stdout);
    }

    public static IReadOnlyList<string> ParseReleaseAssetNames(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<string>();
            foreach (var el in assets.EnumerateArray())
            {
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!string.IsNullOrWhiteSpace(name))
                    list.Add(name);
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static async Task<IReadOnlyList<ReleaseItem>> ListReleaseItemsAsync(ProjectCatalog catalog, GithubConfig cfg, int limit)
    {
        var args = new List<string>
        {
            "release", "list", "--limit", limit.ToString(),
            "--json", "tagName,name,isLatest,isDraft,isPrerelease,publishedAt",
        };
        if (!string.IsNullOrEmpty(cfg.Slug()))
            args.AddRange(["--repo", cfg.Slug()]);
        var (code, stdout, stderr) = await GhCli.RunCaptureAsync(args, catalog.Root, cfg, 60_000).ConfigureAwait(false);
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

    private static string FormatReleaseLine(ReleaseItem item) => item.Line();

    private static string FirstLine(string text)
    {
        var t = (text ?? "").Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }
}
