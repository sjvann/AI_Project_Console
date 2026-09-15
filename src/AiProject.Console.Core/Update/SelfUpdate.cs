using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using AiProject.Console.Core.GitHub;
using AiProject.Console.Core.Util;
using AiProject.Shared.Update;

namespace AiProject.Console.Core.Update;

public enum InstallKind
{
    Development,
    Installed,
    Portable,
}

public enum UpdateApplyMode
{
    None,
    Installer,
    PortableZip,
    OpenReleases,
}

public sealed record ReleaseAsset(string Name, string Url, long Size);

public sealed record AvailableUpdate(
    string Tag,
    string Title,
    string HtmlUrl,
    ReleaseAsset? SetupAsset,
    ReleaseAsset? ZipAsset,
    bool Prerelease = false);

public static class SelfUpdate
{
    private static readonly HttpClient Http = CreateClient();

    public static string RuntimeId()
    {
        if (OperatingSystem.IsWindows())
            return "win-x64";
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }

    public static InstallKind DetectInstallKind(string? baseDir = null)
    {
        var dir = Path.GetFullPath(baseDir ?? AppContext.BaseDirectory);
        var parts = dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!parts[i].Equals("bin", StringComparison.OrdinalIgnoreCase))
                continue;
            if (parts[i + 1].Equals("Debug", StringComparison.OrdinalIgnoreCase)
                || parts[i + 1].Equals("Release", StringComparison.OrdinalIgnoreCase))
                return InstallKind.Development;
        }

        var cur = new DirectoryInfo(dir);
        for (var i = 0; i < 6 && cur is not null; i++, cur = cur.Parent)
        {
            if (File.Exists(Path.Combine(cur.FullName, "AiProject.Console.App.csproj")))
                return InstallKind.Development;
        }

        try
        {
            if (Directory.GetFiles(dir, "unins*.exe").Length > 0)
                return InstallKind.Installed;
        }
        catch (IOException)
        {
            // ignore
        }

        return InstallKind.Portable;
    }

    public static UpdateApplyMode ResolveApplyMode(AvailableUpdate update, InstallKind kind)
    {
        if (kind == InstallKind.Development)
            return UpdateApplyMode.OpenReleases;
        if (kind == InstallKind.Installed && update.SetupAsset is not null)
            return UpdateApplyMode.Installer;
        if (update.ZipAsset is not null)
            return UpdateApplyMode.PortableZip;
        if (update.SetupAsset is not null)
            return UpdateApplyMode.Installer;
        return UpdateApplyMode.OpenReleases;
    }

    public static UpdateApplyMode ResolveLocalFileMode(string path, InstallKind kind)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return UpdateApplyMode.None;
        var name = Path.GetFileName(path);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return UpdateApplyMode.Installer;
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return UpdateApplyMode.PortableZip;
        return UpdateApplyMode.None;
    }

    public static AvailableUpdate? ParseLatest(
        string json,
        string currentVersion,
        string? runtimeId = null,
        bool includePrerelease = false)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            return PickNewest(root, currentVersion, runtimeId, includePrerelease);
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
        {
            var text = msg.GetString() ?? "";
            if (!string.IsNullOrEmpty(text) && !root.TryGetProperty("tag_name", out _))
                throw new InvalidOperationException("GitHub API：" + text);
        }

        var update = TryReadRelease(root, runtimeId);
        if (update is null)
            return null;
        if (update.Prerelease && !includePrerelease)
            return null;
        return ReleaseVersion.IsNewer(update.Tag, currentVersion) ? update : null;
    }

    public static async Task<AvailableUpdate?> CheckLatestAsync(CancellationToken ct = default, bool includePrerelease = false)
    {
        var body = includePrerelease
            ? await FetchReleasesJsonAsync(ct).ConfigureAwait(false)
            : await FetchLatestJsonAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(body)
            ? null
            : ParseLatest(body, AppInfo.Version, includePrerelease: includePrerelease);
    }

    public static async Task<string> DownloadAssetAsync(
        AvailableUpdate update,
        ReleaseAsset asset,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var dest = ReserveUpdatePath(asset.Name);

        if (CliUtil.CommandExists("gh"))
        {
            progress?.Report("下載更新（gh）…");
            var args = new List<string>
            {
                "release", "download", update.Tag,
                "--repo", AppInfo.GitHubSlug,
                "--pattern", asset.Name,
                "--clobber",
                "--output", dest,
            };
            var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
                "gh",
                args,
                timeoutMs: 600_000,
                ct: ct).ConfigureAwait(false);
            if (code != 0 || !File.Exists(dest))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                    ? (string.IsNullOrWhiteSpace(stdout) ? "gh 下載 Release 失敗。" : stdout)
                    : stderr);
            await EnsureChecksumOrWarnAsync(dest, progress, ct).ConfigureAwait(false);
            return dest;
        }

        progress?.Report("下載更新…");
        using var req = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        AttachToken(req);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("無法下載安裝包。倉庫若為私人，請先安裝並登入 GitHub CLI（gh auth login）。");
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? (asset.Size > 0 ? asset.Size : 0);
        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read, 81_920, useAsync: true))
        {
            var buffer = new byte[81_920];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                copied += read;
                if (progress is null)
                    continue;
                progress.Report(total > 0
                    ? $"下載更新 {copied / 1_048_576.0:0.0} / {total / 1_048_576.0:0.0} MB"
                    : $"下載更新 {copied / 1_048_576.0:0.0} MB");
            }
            await dst.FlushAsync(ct).ConfigureAwait(false);
        }
        await EnsureChecksumOrWarnAsync(dest, progress, ct).ConfigureAwait(false);
        return dest;
    }

    static async Task EnsureChecksumOrWarnAsync(string dest, IProgress<string>? progress, CancellationToken ct)
    {
        var ok = await Checksum.TryVerifySidecarAsync(dest, ct).ConfigureAwait(false);
        if (ok is false)
            throw new InvalidOperationException("安裝包 SHA256 與旁檔不符，已中止套用。請改從 GitHub Releases 重新下載。");
        if (ok is true)
            progress?.Report("已核對 SHA256。");
    }

    public static string LaunchApply(string downloadedPath, UpdateApplyMode mode, string? installDir = null, bool silent = true)
    {
        var target = Path.GetFullPath(installDir ?? AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var exe = Path.Combine(target, OperatingSystem.IsWindows() ? AppInfo.ExeName : "AI_Project_Console");
        if (mode == UpdateApplyMode.Installer)
        {
            StartInstaller(downloadedPath, target, silent);
            return silent
                ? "已啟動安裝程式，控制台即將關閉並重開。"
                : "已啟動安裝程式。";
        }

        if (mode != UpdateApplyMode.PortableZip)
            throw new InvalidOperationException("此執行方式無法自動覆蓋，請改從 GitHub Releases 下載。");

        var extractDir = Path.Combine(Path.GetDirectoryName(downloadedPath)!, "extract-" + Guid.NewGuid().ToString("N")[..8]);
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        ZipFile.ExtractToDirectory(downloadedPath, extractDir);
        StartHelperScript(WriteSwapScript(extractDir, target, Environment.ProcessId, exe));
        return "已準備覆蓋檔案，控制台即將關閉並重開。";
    }

    public static string BuildInstallerArguments(string installDir, bool silent, bool pinDirectory)
    {
        var dir = "/DIR=\"" + Path.GetFullPath(installDir) + "\"";
        if (silent)
            return "/SILENT /CLOSEAPPLICATIONS /NORESTART /SUPPRESSMSGBOXES " + dir;
        return pinDirectory ? "/NORESTART " + dir : "/NORESTART";
    }

    public static int StartInstaller(string setupPath, string installDir, bool silent)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("目前只支援在 Windows 啟動安裝程式。");
        try
        {
            var staged = StageUpdateFile(setupPath);
            var pinDir = DetectInstallKind() != InstallKind.Development;
            var args = BuildInstallerArguments(installDir, silent, pinDir);
            var pid = DetachedProcess.Start(staged, args, showWindow: !silent, workingDirectory: Path.GetDirectoryName(staged));
            Thread.Sleep(400);
            return pid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            throw new InvalidOperationException(
                "無法啟動安裝程式：暫存安裝檔被其他程式占用（常見於防毒軟體掃描）。請關閉控制台後，改從 GitHub Releases 手動執行 setup.exe。\n" + ex.Message,
                ex);
        }
    }

    public static string ReserveUpdatePath(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("安裝檔名是空的。");
        var dest = Path.Combine(StagingDirectory(), name);
        if (!File.Exists(dest))
            return dest;
        if (TryRetireFile(dest))
            return dest;
        return Path.Combine(StagingDirectory(), Guid.NewGuid().ToString("N")[..8] + "-" + name);
    }

    public static string StageUpdateFile(string path)
    {
        var src = Path.GetFullPath(path);
        if (!File.Exists(src))
            throw new InvalidOperationException("找不到檔案：" + src);

        // Never copy onto the download itself. Same-path File.Copy on Windows throws
        // "The process cannot access the file because it is being used by another process."
        var dest = AllocateRunPath(Path.GetFileName(src));
        CopyWithRetry(src, dest);
        TryUnblock(dest);
        return dest;
    }

    public static void TryUnblock(string path)
    {
        try
        {
            File.Delete(path + ":Zone.Identifier");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ignore: no MOTW or cannot remove
        }
    }

    public static string BuildInstallerRestartScript(string setupPath, string installDir, int pid, string exePath, bool silent = true)
    {
        var setup = PsQuote(Path.GetFullPath(setupPath));
        var target = PsQuote(Path.GetFullPath(installDir));
        var exe = PsQuote(Path.GetFullPath(exePath));
        var exeName = PsQuote(Path.GetFileNameWithoutExtension(exePath));
        var setupArgs = silent
            ? "@('/SILENT','/CLOSEAPPLICATIONS','/NORESTART','/SUPPRESSMSGBOXES',('/DIR=\"' + $target + '\"'))"
            : "@('/NORESTART',('/DIR=\"' + $target + '\"'))";
        return
            "$ErrorActionPreference = 'Stop'\r\n" +
            "while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }\r\n" +
            "Start-Sleep -Seconds 1\r\n" +
            "$setup = " + setup + "\r\n" +
            "$target = " + target + "\r\n" +
            "Start-Process -FilePath $setup -ArgumentList " + setupArgs + " -Wait\r\n" +
            "Start-Sleep -Seconds 1\r\n" +
            "if (-not (Get-Process -Name " + exeName + " -ErrorAction SilentlyContinue)) {\r\n" +
            "  Start-Process -FilePath " + exe + "\r\n" +
            "}\r\n";
    }

    public static void OpenReleases(string? htmlUrl = null) =>
        CliUtil.OpenUrl(string.IsNullOrWhiteSpace(htmlUrl) ? AppInfo.ReleasesUrl : htmlUrl);

    public static string DevelopmentHint(AvailableUpdate update) =>
        $"發現新版本 {update.Tag}（目前 v{AppInfo.Version}）。\n\n目前是從原始碼／開發目錄執行，無法直接覆蓋。\n請 git pull 後重新編譯，或從 Releases 下載安裝包：\n{update.HtmlUrl}";

    public static string CannotApplyHint(AvailableUpdate update, InstallKind kind)
    {
        if (kind == InstallKind.Development)
            return DevelopmentHint(update);
        var rid = RuntimeId();
        return $"Release {update.Tag} 沒有適用於 {rid} 的安裝檔（需要檔名含 {rid} 且以 -setup.exe 或 .zip 結尾）。\n\n自動更新因此改開 GitHub 頁。請用 GitHub 操作台發行此控制台時附加安裝包。\n{update.HtmlUrl}";
    }

    public static string CannotApplyLocalFileHint(InstallKind kind, string path)
    {
        if (kind == InstallKind.Development)
            return "目前是從原始碼／開發目錄執行，無法用安裝檔覆蓋。\n請 git pull 後重新編譯，或改用已安裝／zip 版。";
        var name = Path.GetFileName(path);
        return $"無法從「{name}」安裝。請選擇安裝程式（.exe）或 zip 壓縮包。";
    }

    public static bool IsSetupAsset(string name, string runtimeId) =>
        name.Contains(runtimeId, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase);

    public static bool IsZipAsset(string name, string runtimeId) =>
        name.Contains(runtimeId, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("-setup", StringComparison.OrdinalIgnoreCase);

    private static ReleaseAsset? ReadAsset(JsonElement el)
    {
        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var url = el.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
            return null;
        var size = el.TryGetProperty("size", out var s) && s.TryGetInt64(out var n64) ? n64 : 0;
        return new ReleaseAsset(name, url, size);
    }

    private static void StartHelperScript(string script)
    {
        var scriptPath = Path.GetFullPath(script);
        if (!OperatingSystem.IsWindows())
        {
            var started = Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "\"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (started is null)
                throw new InvalidOperationException("無法啟動更新輔助程式。");
            return;
        }

        DetachedProcess.Start(
            PowerShellExe(),
            "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
            showWindow: false,
            workingDirectory: Path.GetDirectoryName(scriptPath));
    }

    private static string PowerShellExe()
    {
        var system = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        return File.Exists(system) ? system : "powershell.exe";
    }

    private static string WriteSwapScript(string extractDir, string targetDir, int pid, string exePath)
    {
        var script = Path.Combine(StagingDirectory(), "apply.ps1");
        var src = PsQuote(extractDir);
        var dst = PsQuote(targetDir);
        var exe = PsQuote(exePath);
        File.WriteAllText(script,
            "$ErrorActionPreference = 'Stop'\r\n" +
            "while (Get-Process -Id " + pid + " -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }\r\n" +
            "Start-Sleep -Seconds 1\r\n" +
            "Copy-Item -Path (Join-Path " + src + " '*') -Destination " + dst + " -Recurse -Force\r\n" +
            "Start-Process -FilePath " + exe + "\r\n");
        return script;
    }

    private static string PsQuote(string path) => "'" + path.Replace("'", "''") + "'";

    private static string StagingDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "AI_Project_Console-update");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string AllocateRunPath(string fileName)
    {
        var dir = Path.Combine(StagingDirectory(), "run-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileName);
    }

    private static void CopyWithRetry(string src, string dest)
    {
        if (PathsEqual(src, dest))
            dest = AllocateRunPath(Path.GetFileName(src));

        Exception? last = null;
        for (var i = 0; i < 8; i++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: false);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                dest = AllocateRunPath(Path.GetFileName(src));
                Thread.Sleep(150);
            }
        }

        throw new InvalidOperationException(
            "無法複製安裝檔（檔案被占用）。請關閉控制台後，改從 GitHub Releases 手動執行 setup.exe。",
            last);
    }

    private static bool TryRetireFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name))
            return false;

        for (var i = 0; i < 8; i++)
        {
            var retired = Path.Combine(dir, name + ".old-" + Guid.NewGuid().ToString("N")[..8] + ext);
            try
            {
                if (!File.Exists(path))
                    return true;
                File.Move(path, retired);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(150);
            }
        }
        return !File.Exists(path);
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static AvailableUpdate? PickNewest(
        JsonElement releases,
        string currentVersion,
        string? runtimeId,
        bool includePrerelease)
    {
        AvailableUpdate? best = null;
        SemVer? bestVer = null;
        foreach (var el in releases.EnumerateArray())
        {
            var update = TryReadRelease(el, runtimeId);
            if (update is null)
                continue;
            if (update.Prerelease && !includePrerelease)
                continue;
            if (!ReleaseVersion.TryParse(update.Tag, out var ver))
                continue;
            if (!ReleaseVersion.IsNewer(update.Tag, currentVersion))
                continue;
            if (bestVer is not null && ReleaseVersion.Compare(ver, bestVer.Value) <= 0)
                continue;
            best = update;
            bestVer = ver;
        }
        return best;
    }

    private static AvailableUpdate? TryReadRelease(JsonElement root, string? runtimeId)
    {
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            return null;
        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var title = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var html = root.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() ?? "" : "";
        var rid = string.IsNullOrWhiteSpace(runtimeId) ? RuntimeId() : runtimeId;
        ReleaseAsset? setup = null;
        ReleaseAsset? zip = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in assets.EnumerateArray())
            {
                var asset = ReadAsset(el);
                if (asset is null)
                    continue;
                if (IsSetupAsset(asset.Name, rid))
                    setup = asset;
                else if (IsZipAsset(asset.Name, rid))
                    zip = asset;
            }
        }

        return new AvailableUpdate(
            Tag: tag,
            Title: string.IsNullOrWhiteSpace(title) ? tag : title,
            HtmlUrl: string.IsNullOrWhiteSpace(html) ? AppInfo.ReleasesUrl : html,
            SetupAsset: setup,
            ZipAsset: zip,
            Prerelease: IsPrereleaseRelease(root, tag));
    }

    private static bool IsPrereleaseRelease(JsonElement root, string tag)
    {
        if (root.TryGetProperty("prerelease", out var flag) && flag.ValueKind == JsonValueKind.True)
            return true;
        return ReleaseVersion.TryParse(tag, out var ver) && ver.HasPreRelease;
    }

    private static Task<string?> FetchLatestJsonAsync(CancellationToken ct) =>
        FetchReleaseApiAsync($"repos/{AppInfo.GitHubSlug}/releases/latest", AppInfo.LatestReleaseApiUrl, ct);

    private static Task<string?> FetchReleasesJsonAsync(CancellationToken ct) =>
        FetchReleaseApiAsync($"repos/{AppInfo.GitHubSlug}/releases?per_page=30", AppInfo.ReleasesApiUrl, ct);

    private static async Task<string?> FetchReleaseApiAsync(string ghPath, string httpUrl, CancellationToken ct)
    {
        if (CliUtil.CommandExists("gh"))
        {
            var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
                "gh",
                ["api", ghPath],
                timeoutMs: 30_000,
                ct: ct).ConfigureAwait(false);
            if (code != 0)
            {
                var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                if (LooksLikeNotFound(err))
                    return null;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err)
                    ? "無法查詢 GitHub Releases。"
                    : FirstLine(err));
            }
            return stdout;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, httpUrl);
        AttachToken(req);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            if (string.IsNullOrEmpty(GithubToken()))
                throw new InvalidOperationException(
                    "無法讀取 GitHub Releases（倉庫可能為私人）。請安裝 GitHub CLI 並執行 gh auth login。");
            return null;
        }
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
                ? $"無法查詢 GitHub Releases（HTTP {(int)resp.StatusCode}）。"
                : FirstLine(body));
        return body;
    }

    private static void AttachToken(HttpRequestMessage req)
    {
        var token = GithubToken();
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static string GithubToken() =>
        JsonUtil.Pick(Environment.GetEnvironmentVariable("GH_TOKEN"), Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    private static bool LooksLikeNotFound(string text) =>
        text.Contains("Not Found", StringComparison.OrdinalIgnoreCase)
        || text.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase)
        || text.Contains("\"status\": \"404\"", StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string text)
    {
        var t = (text ?? "").Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.SettingsDirName}/{AppInfo.Version}");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }
}
