using System.Diagnostics;
using System.Text.RegularExpressions;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Cursor;

public static class CursorLauncher
{
    private static readonly Regex ErrorLine = new(@"\berror\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const int MaxLogChars = 8_000;
    private const int MaxErrorLines = 80;
    private const string DeeplinkBase = "cursor://anysphere.cursor-deeplink/prompt";
    private const int DeeplinkMaxChars = 2048;
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
    };

    public static string? ResolveCli()
    {
        var found = CliUtil.FindOnPath("cursor");
        if (found is not null)
            return found;
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetEnvironmentVariable("LOCALAPPDATA")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
            var cmd = Path.Combine(local, "Programs", "cursor", "resources", "app", "bin", "cursor.cmd");
            if (File.Exists(cmd))
                return cmd;
        }
        return null;
    }

    public static bool IsCursorRunning()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return Process.GetProcessesByName("Cursor").Length > 0 || Process.GetProcessesByName("cursor").Length > 0;
            if (OperatingSystem.IsMacOS())
            {
                var p = Process.Start(new ProcessStartInfo("pgrep", "-x Cursor") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit(10_000);
                return p?.ExitCode == 0;
            }
            var proc = Process.Start(new ProcessStartInfo("pgrep", "-f cursor") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            proc?.WaitForExit(10_000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static int NewAgentLaunchDelayMs() => IsCursorRunning() ? 400 : 2200;

    public static string? OpenInCursor(string path, bool reuseWindow = true, IEnumerable<string>? extraPaths = null)
    {
        var cli = ResolveCli();
        if (cli is null)
            return "找不到 Cursor CLI（請確認已安裝並把 cursor 加到 PATH）";
        var target = Path.GetFullPath(path);
        if (!File.Exists(target) && !Directory.Exists(target))
            return $"路徑不存在：{target}";
        var psi = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (reuseWindow)
            psi.ArgumentList.Add("--reuse-window");
        psi.ArgumentList.Add(Directory.Exists(target) ? target : Path.GetDirectoryName(target)!);
        if (extraPaths is not null)
        {
            foreach (var extra in extraPaths)
            {
                if (File.Exists(extra))
                    psi.ArgumentList.Add(Path.GetFullPath(extra));
            }
        }
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return $"無法啟動 Cursor：{ex.Message}";
        }
        return null;
    }

    public static string? CloseCursor()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var name in new[] { "Cursor", "cursor" })
                {
                    foreach (var p in Process.GetProcessesByName(name))
                    {
                        try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                    }
                }
                return null;
            }
            if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("osascript", "-e \"tell application \\\"Cursor\\\" to quit\"") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(30_000);
                return null;
            }
            Process.Start(new ProcessStartInfo("pkill", "-f cursor") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(15_000);
            return null;
        }
        catch (Exception ex)
        {
            return $"關閉 Cursor 失敗：{ex.Message}";
        }
    }

    public static IReadOnlyList<string> ExtractBuildErrors(string logText)
    {
        var lines = logText.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        var errors = lines.Where(ln => ErrorLine.IsMatch(ln) && !ln.Contains("error(s)", StringComparison.OrdinalIgnoreCase)).Take(MaxErrorLines).ToList();
        if (errors.Count > 0)
            return errors;
        return lines.TakeLast(Math.Min(40, lines.Count)).ToList();
    }

    public static string BuildAgentPrompt(string root, string target, int exitCode, string logText)
    {
        var errors = ExtractBuildErrors(logText);
        var logTrim = logText;
        if (logTrim.Length > MaxLogChars)
            logTrim = "…（前略）…\n" + logTrim[^MaxLogChars..];
        var errorBlock = errors.Count > 0
            ? string.Join('\n', errors.Select(e => "- " + e))
            : "- （未能解析具體 error 行，請見下方輸出）";
        return
            "建置失敗，請找出根因並直接修改程式碼讓建置通過。優先處理 error，不要只做說明。"
            + "改完後用同樣目標再確認能建置成功。\n\n"
            + $"專案根目錄：{root}\n"
            + $"建置目標：{target}\n"
            + $"結束碼：{exitCode}\n\n"
            + $"錯誤摘要：\n{errorBlock}\n\n"
            + $"完整建置輸出：\n```text\n{logTrim.TrimEnd()}\n```\n";
    }

    public static (string ReportDir, List<string> Copied) StageUatScreenshots(string reportsDir, string title, IEnumerable<string> sourceImages)
    {
        Directory.CreateDirectory(reportsDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeTitle = Regex.Replace((title ?? "uat").Trim(), @"[^\w\u4e00-\u9fff\-]+", "-").Trim('-');
        if (safeTitle.Length > 40)
            safeTitle = safeTitle[..40].Trim('-');
        if (string.IsNullOrEmpty(safeTitle))
            safeTitle = "uat";
        var reportDir = Path.Combine(reportsDir, $"{stamp}-{safeTitle}");
        Directory.CreateDirectory(reportDir);
        var copied = new List<string>();
        var i = 1;
        foreach (var src in sourceImages)
        {
            if (!File.Exists(src))
                continue;
            var ext = Path.GetExtension(src);
            if (!ImageExts.Contains(ext))
                ext = ".png";
            var dest = Path.Combine(reportDir, $"screenshot-{i:00}{ext}");
            File.Copy(src, dest, overwrite: true);
            copied.Add(dest);
            i++;
        }
        return (reportDir, copied);
    }

    public static string BuildUatAgentPrompt(string root, string title, string description, IEnumerable<string> imagePaths)
    {
        var desc = string.IsNullOrWhiteSpace(description) ? "（未填寫說明）" : description.Trim();
        var lines = new List<string>
        {
            "UAT 測試發現問題。請依下列說明與截圖找出根因並直接修改程式碼；優先修復，不要只做說明。",
            "改完後簡短說明改了什麼、如何驗證。",
            "",
            $"專案根目錄：{root}",
            $"標題：{(string.IsNullOrWhiteSpace(title) ? "（未命名）" : title.Trim())}",
            "",
            "問題說明：",
            desc,
        };
        var images = imagePaths.Take(8).ToList();
        if (images.Count > 0)
        {
            lines.Add("");
            lines.Add("請用 Read 查看這些截圖：");
            foreach (var path in images)
                lines.Add("- " + Path.GetFullPath(path).Replace('\\', '/'));
        }
        return string.Join('\n', lines) + "\n";
    }

    public static string PromptDeeplinkUrl(string promptText) =>
        DeeplinkBase + "?text=" + Uri.EscapeDataString(promptText);

    public static string FitPromptForDeeplink(string prompt, int limit = DeeplinkMaxChars)
    {
        const string suffix = "\n\n…（內容過長，已截斷；完整內容在剪貼簿，可於 New Agent 貼上）";
        if (PromptDeeplinkUrl(prompt).Length <= limit)
            return prompt;
        var lo = 0;
        var hi = prompt.Length;
        var best = suffix;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var cand = prompt[..mid].TrimEnd() + suffix;
            if (PromptDeeplinkUrl(cand).Length <= limit)
            {
                best = cand;
                lo = mid + 1;
            }
            else
                hi = mid - 1;
        }
        return best;
    }

    public static string? OpenPromptDeeplink(string prompt)
    {
        var url = PromptDeeplinkUrl(FitPromptForDeeplink(prompt));
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex)
        {
            return $"無法開啟 Cursor New Agent：{ex.Message}";
        }
        return null;
    }

    public static string? OpenProjectForNewAgent(string root) => OpenInCursor(root, reuseWindow: true);

    public static string? SaveClipboardImageWindows(string dest)
    {
        if (!OperatingSystem.IsWindows())
            return "此平台請改用「加入檔案」";
        var escaped = dest.Replace("'", "''");
        var ps =
            "Add-Type -AssemblyName System.Windows.Forms\n" +
            "Add-Type -AssemblyName System.Drawing\n" +
            "$img = [System.Windows.Forms.Clipboard]::GetImage()\n" +
            "if ($null -eq $img) { exit 2 }\n" +
            "$img.Save('" + escaped + "', [System.Drawing.Imaging.ImageFormat]::Png)\n" +
            "exit 0";
        try
        {
            var (code, output) = CliUtil.RunAsync("powershell", ["-NoProfile", "-Command", ps], timeoutMs: 20_000).GetAwaiter().GetResult();
            if (code == 2)
                return "剪貼簿沒有圖片（請先截圖：Win+Shift+S 後再貼上）";
            if (code != 0)
                return string.IsNullOrEmpty(output) ? "無法從剪貼簿存圖" : output;
            if (!File.Exists(dest) || new FileInfo(dest).Length <= 0)
                return "剪貼簿圖片寫入失敗";
            return null;
        }
        catch (Exception ex)
        {
            return $"無法讀取剪貼簿：{ex.Message}";
        }
    }
}
