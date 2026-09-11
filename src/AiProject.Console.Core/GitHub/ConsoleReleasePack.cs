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

    public const string InnoSetupUrl = "https://jrsoftware.org/isinfo.php";

    public static string InnoMissingMessage =>
        "找不到 Inno Setup（ISCC.exe）。請安裝 Inno Setup 6 或 7：" + InnoSetupUrl
        + "\n安裝後請關閉並重開控制台再發行。";

    public static string? FindIscc()
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };
        string[] rels = ["Inno Setup 7\\ISCC.exe", "Inno Setup 6\\ISCC.exe"];
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            foreach (var rel in rels)
            {
                var path = Path.Combine(root, rel);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
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

        Report(progress, "version", $"正在寫入版號 v{version}…");
        SyncVersionFiles(root, version);

        Report(progress, "inno", "正在檢查 Inno Setup…");
        if (string.IsNullOrEmpty(FindIscc()))
            throw new InvalidOperationException(InnoMissingMessage);

        Report(progress, "publish", "正在編譯 Windows 執行檔（可能要 1–3 分鐘）…");
        var script = PackScript(root);
        var (code, stdout, stderr) = await CliUtil.RunCaptureAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Version", version],
            cwd: Path.GetFullPath(root),
            timeoutMs: 900_000,
            ct: ct,
            onLine: progress is null ? null : line => ForwardPackLine(progress, line)).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(FormatPackError(stdout, stderr));

        var found = FindExisting(root, tag);
        if (!found.HasSetup)
            throw new InvalidOperationException(
                $"打包完成但找不到 {SetupFileName(version)}。請確認已安裝 Inno Setup 6 或 7。");
        Report(progress, "installer", "安裝包已產出。");
        return found;
    }

    public static (string? StageId, string? UserLine) InterpretPackLine(string? line)
    {
        var t = (line ?? "").Trim();
        if (t.Length == 0)
            return (null, null);

        if (t.StartsWith("PACK:", StringComparison.OrdinalIgnoreCase))
        {
            var id = t[5..].Trim().ToLowerInvariant();
            return id switch
            {
                "inno" => ("inno", "正在檢查 Inno Setup…"),
                "publish" => ("publish", "正在編譯 Windows 執行檔（可能要 1–3 分鐘）…"),
                "zip" => ("zip", "正在壓縮免安裝包…"),
                "installer" => ("installer", "正在編譯安裝程式…"),
                "done" => ("installer", "安裝包已產出"),
                _ => (null, t),
            };
        }

        if (IsPackNoise(t))
            return (null, null);

        if (t.Contains("找不到 Inno", StringComparison.OrdinalIgnoreCase))
            return ("inno", InnoMissingMessage.Split('\n')[0]);
        if (t.StartsWith("Publishing", StringComparison.OrdinalIgnoreCase))
            return ("publish", "正在編譯 Windows 執行檔（可能要 1–3 分鐘）…");
        if (t.Contains("Determining projects to restore", StringComparison.OrdinalIgnoreCase))
            return ("publish", "正在還原 NuGet 套件…");
        if (t.Contains("Restore complete", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Restored ", StringComparison.OrdinalIgnoreCase))
            return ("publish", "套件已還原，開始編譯…");
        if (t.Contains(" -> ", StringComparison.Ordinal))
            return ("publish", "正在輸出執行檔…");
        if (t.StartsWith("Building installer", StringComparison.OrdinalIgnoreCase))
            return ("installer", "正在編譯安裝程式…");
        if (t.Equals("Done.", StringComparison.OrdinalIgnoreCase))
            return ("installer", "安裝包已產出");
        if (t.Contains("dotnet publish failed", StringComparison.OrdinalIgnoreCase)
            || t.Contains("dotnet publish 失敗", StringComparison.OrdinalIgnoreCase))
            return ("publish", "編譯發布失敗。");
        if (t.Contains("Inno Setup compile failed", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Inno Setup 編譯失敗", StringComparison.OrdinalIgnoreCase))
            return ("installer", "安裝程式編譯失敗。");
        if (LooksLikePackError(t))
            return (null, t.Length <= 400 ? t : t[..397] + "…");

        return (null, t.Length <= 200 ? t : t[..197] + "…");
    }

    public static string? InterpretGhLine(string? line)
    {
        var t = (line ?? "").Trim();
        if (t.Length == 0 || IsPackNoise(t))
            return null;
        if (t.Contains("Uploading", StringComparison.OrdinalIgnoreCase)
            || t.Contains("uploading", StringComparison.OrdinalIgnoreCase))
            return ReleaseRunState.StageLine("upload", "正在上傳安裝包…");
        if (t.Contains("https://", StringComparison.OrdinalIgnoreCase)
            && t.Contains("/releases/", StringComparison.OrdinalIgnoreCase))
            return ReleaseRunState.StageLine("upload", "Release 已建立：" + t);
        if (LooksLikePackError(t))
            return t.Length <= 400 ? t : t[..397] + "…";
        return t.Length <= 200 ? t : t[..197] + "…";
    }

    public static string FormatPackError(string? stdout, string? stderr)
    {
        var err = JoinOutput(stdout, stderr);
        if (string.IsNullOrWhiteSpace(err))
            return "打包 Windows 安裝包失敗。請看進度視窗的紀錄。";
        if (err.Contains("找不到 Inno", StringComparison.OrdinalIgnoreCase)
            || err.Contains("ISCC.exe", StringComparison.OrdinalIgnoreCase)
               && err.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return InnoMissingMessage;
        return Tail(err, 4000);
    }

    static void Report(IProgress<string>? progress, string stageId, string message) =>
        progress?.Report(ReleaseRunState.StageLine(stageId, message));

    static void ForwardPackLine(IProgress<string> progress, string line)
    {
        var (stage, user) = InterpretPackLine(line);
        if (string.IsNullOrEmpty(user))
            return;
        progress.Report(string.IsNullOrEmpty(stage)
            ? user
            : ReleaseRunState.StageLine(stage, user));
    }

    static bool IsPackNoise(string t)
    {
        if (t.Contains("Workload updates are available", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("Use dotnet workload", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains("Time Elapsed", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.StartsWith("Copyright (C)", StringComparison.OrdinalIgnoreCase))
            return true;
        if (t.Contains(" warning ", StringComparison.OrdinalIgnoreCase)
            && !t.Contains(" error ", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static bool LooksLikePackError(string t) =>
        t.Contains(" error ", StringComparison.OrdinalIgnoreCase)
        || t.StartsWith("error ", StringComparison.OrdinalIgnoreCase)
        || t.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
        || t.Contains("Exception", StringComparison.OrdinalIgnoreCase)
        || t.Contains("失敗", StringComparison.Ordinal)
        || t.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase);

    static string JoinOutput(string? stdout, string? stderr)
    {
        var a = (stdout ?? "").Trim();
        var b = (stderr ?? "").Trim();
        if (string.IsNullOrEmpty(b))
            return a;
        if (string.IsNullOrEmpty(a))
            return b;
        return a + "\n" + b;
    }

    static string Tail(string text, int max)
    {
        var t = text.Trim();
        return t.Length <= max ? t : "…（略過較早輸出）\n" + t[^(max - 16)..];
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
}
