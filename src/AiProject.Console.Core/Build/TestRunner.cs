using System.Diagnostics;
using System.Text;
using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Build;

/// <summary>
/// 本機一次跑完整測試（方案或測試專案）。不是 IDE 的測試總管。
/// </summary>
public static class TestRunner
{
    public static string? FindSolution(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return null;
        root = Path.GetFullPath(root);
        return PickOne(Directory.GetFiles(root, "*.slnx"), root)
            ?? PickOne(Directory.GetFiles(root, "*.sln"), root);
    }

    public static IReadOnlyList<string> TargetsFor(ProjectCatalog catalog)
    {
        var sln = FindSolution(catalog.Root);
        if (!string.IsNullOrEmpty(sln))
            return [sln];
        return catalog.Projects
            .Where(p => p.IsTest)
            .Select(p => p.Csproj)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task<(int ExitCode, string Log)> TestAsync(
        string cwd,
        string target,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        var lines = new List<string>();
        void Emit(string line)
        {
            lines.Add(line);
            progress?.Report(line);
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("test");
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("minimal");
        psi.ArgumentList.Add("--nologo");

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var combined = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        foreach (var line in combined.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.Length > 0)
                Emit(text);
        }
        return (proc.ExitCode, string.Join('\n', lines));
    }

    static string? PickOne(string[] files, string root)
    {
        if (files.Length == 0)
            return null;
        if (files.Length == 1)
            return files[0];
        var folder = Path.GetFileName(root);
        return files.FirstOrDefault(p =>
                   string.Equals(Path.GetFileNameWithoutExtension(p), folder, StringComparison.OrdinalIgnoreCase))
               ?? files[0];
    }
}
