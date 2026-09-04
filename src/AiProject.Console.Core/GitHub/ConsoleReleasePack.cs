using System.Text.RegularExpressions;
using AiProject.Console.Core.Update;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.GitHub;

public sealed record PackedReleaseAssets(
    string Version,
    string RuntimeId,
    string? SetupPath,
    string? ZipPath)
{
    public bool HasSetup => !string.IsNullOrEmpty(SetupPath) && File.Exists(SetupPath);

    public bool HasZip => !string.IsNullOrEmpty(ZipPath) && File.Exists(ZipPath);

    public IReadOnlyList<string> ExistingPaths()
    {
        var list = new List<string>();
        if (HasSetup)
            list.Add(SetupPath!);
        if (HasZip)
            list.Add(ZipPath!);
        return list;
    }
}

/// <summary>
/// 此控制台自己的 Windows 安裝包。自動更新依 GitHub Release 的
/// <c>*-win-x64-setup.exe</c> 啟動安裝程式；沒有這個檔就會改開 Releases 頁。
/// </summary>
public static class ConsoleReleasePack
{
    public const string WindowsRuntime = "win-x64";

    public static bool LooksPackable(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return false;
        return File.Exists(PackScript(root)) && File.Exists(IssPath(root));
    }

    public static string PackScript(string root) =>
        Path.Combine(Path.GetFullPath(root), "scripts", "pack-win.ps1");

    public static string IssPath(string root) =>
        Path.Combine(Path.GetFullPath(root), "installer", "windows", "setup.iss");

    public static string BareVersion(string? tag)
    {
        var raw = (tag ?? "").Trim();
        if (ReleaseVersion.TryParse(raw, out var ver))
        {
            var core = $"{ver.Major}.{ver.Minor}.{ver.Patch}";
            return ver.HasPreRelease ? core + "-" + ver.PreRelease : core;
        }
        return raw.TrimStart('v', 'V');
    }

    public static string SetupFileName(string version, string runtimeId = WindowsRuntime) =>
        $"AI_Project_Console-{version}-{runtimeId}-setup.exe";

    public static string ZipFileName(string version, string runtimeId = WindowsRuntime) =>
        $"AI_Project_Console-{version}-{runtimeId}.zip";

    public static PackedReleaseAssets FindExisting(string root, string tag, string runtimeId = WindowsRuntime)
    {
        var version = BareVersion(tag);
        var dist = Path.Combine(Path.GetFullPath(root), "dist");
        var setup = Path.Combine(dist, SetupFileName(version, runtimeId));
        var zip = Path.Combine(dist, ZipFileName(version, runtimeId));
        return new PackedReleaseAssets(
            version,
            runtimeId,
            File.Exists(setup) ? setup : null,
            File.Exists(zip) ? zip : null);
    }

    public static bool HasSetupAsset(IEnumerable<string>? paths, string runtimeId = WindowsRuntime) =>
        (paths ?? []).Any(p => SelfUpdate.IsSetupAsset(Path.GetFileName(p), runtimeId));

    public static bool HasZipAsset(IEnumerable<string>? paths, string runtimeId = WindowsRuntime) =>
        (paths ?? []).Any(p => SelfUpdate.IsZipAsset(Path.GetFileName(p), runtimeId));

    public static bool RequiresInstaller(bool packable, bool draft) => packable && !draft;

    public static bool IsConsoleDistAsset(string path)
    {
        var name = Path.GetFileName(path);
        return SelfUpdate.IsSetupAsset(name, WindowsRuntime)
            || SelfUpdate.IsZipAsset(name, WindowsRuntime);
    }

    public static string DownloadNotes(PackedReleaseAssets packed)
    {
        var lines = new List<string> { "### 下載", "" };
        if (packed.HasSetup)
            lines.Add($"- **{Path.GetFileName(packed.SetupPath)}**：安裝程式（開始選單捷徑，可選桌面捷徑；安裝到目前使用者的 Local AppData）");
        if (packed.HasZip)
            lines.Add($"- **{Path.GetFileName(packed.ZipPath)}**：免安裝壓縮包，解壓後執行 `{AppInfo.ExeName}`");
        lines.Add("");
        lines.Add("Windows 10/11 需已安裝 [WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)（系統通常已內建）。");
        return string.Join('\n', lines);
    }

    public static string MergeNotes(string? userNotes, PackedReleaseAssets packed)
    {
        var extra = (userNotes ?? "").Trim();
        if (!packed.HasSetup && !packed.HasZip)
            return extra;
        if (extra.Contains("-setup.exe", StringComparison.OrdinalIgnoreCase))
            return extra;
        var download = DownloadNotes(packed);
        return string.IsNullOrEmpty(extra) ? download : extra + "\n\n" + download;
    }

    public static PackedReleaseAssets FromAssetPaths(string tag, IEnumerable<string> paths, string runtimeId = WindowsRuntime)
    {
        var version = BareVersion(tag);
        string? setup = null;
        string? zip = null;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            if (SelfUpdate.IsSetupAsset(name, runtimeId))
                setup = path;
            else if (SelfUpdate.IsZipAsset(name, runtimeId))
                zip = path;
        }
        return new PackedReleaseAssets(version, runtimeId, setup, zip);
    }

    public static string StampVersion(string text, string kind, string version)
    {
        var ver = (version ?? "").Trim();
        if (string.IsNullOrEmpty(ver) || string.IsNullOrEmpty(text))
            return text;
        return kind switch
        {
            "appinfo" => ReplaceOnce(text, @"public const string Version = ""[^""]+""", $"public const string Version = \"{ver}\""),
            "csproj" => StampCsproj(text, ver),
            "iss" => ReplaceOnce(text, @"#define MyAppVersion ""[^""]+""", $"#define MyAppVersion \"{ver}\""),
            "ps1" => ReplaceOnce(text, @"\[string\]\$Version = ""[^""]+""", $"[string]$Version = \"{ver}\""),
            _ => text,
        };
    }

    public static bool SyncVersionFiles(string root, string version)
    {
        var changed = false;
        foreach (var (rel, kind) in VersionStampRels)
        {
            var path = Path.Combine(Path.GetFullPath(root), rel);
            if (!File.Exists(path))
                continue;
            var before = File.ReadAllText(path);
            var after = StampVersion(before, kind, version);
            if (after == before)
                continue;
            File.WriteAllText(path, after);
            changed = true;
        }
        return changed;
    }

    public static async Task<PackedReleaseAssets> PackAsync(
        string root,
        string tag,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!LooksPackable(root))
            throw new InvalidOperationException("這個專案沒有 Windows 安裝包腳本（scripts/pack-win.ps1）。");
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Windows 安裝包需在 Windows 上打包（Inno Setup）。");

        var version = BareVersion(tag);
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("請先填寫有效版號。");

        SyncVersionFiles(root, version);
        progress?.Report($"打包 Windows 安裝包 v{version}…");
        var script = PackScript(root);
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Version", version],
            cwd: Path.GetFullPath(root),
            timeoutMs: 900_000,
            ct: ct,
            onLine: progress is null ? null : line => progress.Report(TrimPackLine(line))).ConfigureAwait(false);
        if (code != 0)
        {
            var err = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(err)
                ? "打包 Windows 安裝包失敗。"
                : FirstLine(err));
        }

        var found = FindExisting(root, tag);
        if (!found.HasSetup)
            throw new InvalidOperationException(
                $"打包完成但找不到 {SetupFileName(version)}。請確認已安裝 Inno Setup 6。");
        return found;
    }

    private static readonly (string Rel, string Kind)[] VersionStampRels =
    [
        (Path.Combine("src", "AiProject.Console.Core", "AppInfo.cs"), "appinfo"),
        (Path.Combine("src", "AiProject.Console.App", "AiProject.Console.App.csproj"), "csproj"),
        (Path.Combine("installer", "windows", "setup.iss"), "iss"),
        (Path.Combine("scripts", "pack-win.ps1"), "ps1"),
    ];

    private static string StampCsproj(string text, string version)
    {
        text = ReplaceOnce(text, @"<Version>[^<]+</Version>", $"<Version>{version}</Version>");
        return ReplaceOnce(text, @"<InformationalVersion>[^<]+</InformationalVersion>", $"<InformationalVersion>{version}</InformationalVersion>");
    }

    private static string ReplaceOnce(string text, string pattern, string replacement) =>
        new Regex(pattern).Replace(text, replacement, 1);

    private static string TrimPackLine(string line)
    {
        var t = (line ?? "").Trim();
        return t.Length <= 80 ? t : t[..77] + "…";
    }

    private static string FirstLine(string text)
    {
        var t = (text ?? "").Trim();
        var i = t.IndexOfAny(['\r', '\n']);
        return i < 0 ? t : t[..i];
    }
}
