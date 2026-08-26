using System.Text;
using System.Text.Json;
using AiProject.Console.Core.Build;

namespace AiProject.Console.Core.GitHub;

public sealed record CiScaffoldResult(bool Created, string Path, string Message);

/// <summary>
/// 本機倉裡的 GitHub Actions 檔。可寫入建置／測試骨架，不代跑、不覆蓋既有檔。
/// </summary>
public static class CiWorkflow
{
    public const string RelPath = ".github/workflows/ci.yml";

    public static string FilePath(string root) =>
        Path.Combine(Path.GetFullPath(root), ".github", "workflows", "ci.yml");

    public static IReadOnlyList<string> ListFiles(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return [];
        var dir = Path.Combine(Path.GetFullPath(root), ".github", "workflows");
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir)
            .Where(p =>
            {
                var ext = Path.GetExtension(p);
                return ext.Equals(".yml", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase);
            })
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    public static bool HasAny(string? root) => ListFiles(root).Count > 0;

    public static bool HasBuildTest(string? root) =>
        ListFiles(root).Any(LooksLikeBuildTest);

    public static string DoctorLine(string? root)
    {
        var files = ListFiles(root);
        if (files.Count == 0)
            return "CI workflow: 無";
        var joined = string.Join("、", files);
        return HasBuildTest(root)
            ? "CI workflow: 有（" + joined + "）"
            : "CI workflow: 僅文件／其他（" + joined + "）";
    }

    public static (string Label, string Badge, bool BuildTest) Describe(string? root)
    {
        var files = ListFiles(root);
        if (files.Count == 0)
            return ("尚未設定", "無", false);
        if (HasBuildTest(root))
            return ("已設定：" + string.Join("、", files), "有", true);
        return ("僅文件／其他：" + string.Join("、", files), "文件", false);
    }

    public static CiScaffoldResult Ensure(string projectRoot, string? defaultBranch = null)
    {
        var path = FilePath(projectRoot);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            return new(false, path, "已有 .github/workflows/ci.yml，未覆蓋。請在 IDE 審查既有檔。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var slnRel = RelativeSolution(projectRoot);
        var useGlobal = HasGlobalJson(projectRoot);
        File.WriteAllText(
            path,
            Template(slnRel, useGlobal, DetectDotnetVersion(projectRoot), defaultBranch),
            new UTF8Encoding(false));
        return new(true, path, "已寫入 .github/workflows/ci.yml。請審查後提交；控制台不會代跑 Actions。");
    }

    public static bool HasGlobalJson(string? root) =>
        !string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(Path.GetFullPath(root), "global.json"));

    public static string DetectDotnetVersion(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return "10.0.x";
        var path = Path.Combine(Path.GetFullPath(root), "global.json");
        if (!File.Exists(path))
            return "10.0.x";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("sdk", out var sdk)
                || !sdk.TryGetProperty("version", out var ver))
                return "10.0.x";
            var raw = ver.GetString();
            if (string.IsNullOrWhiteSpace(raw))
                return "10.0.x";
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[0] + "." + parts[1] + ".x" : raw.Trim();
        }
        catch
        {
            return "10.0.x";
        }
    }

    public static string? RelativeSolution(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var sln = TestRunner.FindSolution(root);
        if (string.IsNullOrEmpty(sln))
            return null;
        return Path.GetRelativePath(root, sln).Replace('\\', '/');
    }

    public static string Template(
        string? solutionRel = null,
        bool useGlobalJson = false,
        string? dotnetVersion = null,
        string? defaultBranch = null)
    {
        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim();
        var sln = string.IsNullOrWhiteSpace(solutionRel) ? "" : " " + solutionRel.Trim();
        var version = string.IsNullOrWhiteSpace(dotnetVersion) ? "10.0.x" : dotnetVersion.Trim();
        var setup = useGlobalJson
            ? "                      global-json-file: global.json"
            : "                      dotnet-version: \"" + version + "\"";
        return """
            name: CI

            on:
              push:
                branches: [BRANCH]
              pull_request:
                branches: [BRANCH]
              workflow_dispatch:

            permissions:
              contents: read

            concurrency:
              group: ci-${{ github.ref }}
              cancel-in-progress: true

            jobs:
              test:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout
                    uses: actions/checkout@v4

                  - name: Setup .NET
                    uses: actions/setup-dotnet@v4
                    with:
            SETUP

                  - name: Restore
                    run: dotnet restoreSLN

                  - name: Build
                    run: dotnet buildSLN --no-restore -c Release

                  - name: Test
                    run: dotnet testSLN --no-build -c Release --verbosity minimal --nologo
            """.Replace("BRANCH", branch).Replace("SETUP", setup).Replace("SLN", sln).Replace("\r\n", "\n") + "\n";
    }

    static bool LooksLikeBuildTest(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem))
            return false;
        if (stem.Equals("docs", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("pages", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("docfx", StringComparison.OrdinalIgnoreCase))
            return false;
        return stem.Equals("ci", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("test", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("build", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("check", StringComparison.OrdinalIgnoreCase);
    }
}
