using System.Security.Cryptography;
using System.Text;

namespace AiProject.Shared.Update;

/// <summary>發行產物 SHA256 旁檔（K0-3／AD-20）。缺檔時校驗回 false 但不擲錯，由呼叫端決定是否硬擋。</summary>
public static class Checksum
{
    public static string SidecarPath(string artifactPath) => artifactPath + ".sha256";

    public static async Task WriteSidecarAsync(string artifactPath, CancellationToken ct = default)
    {
        var hash = await ComputeFileSha256Async(artifactPath, ct).ConfigureAwait(false);
        var name = Path.GetFileName(artifactPath);
        var line = $"{hash}  {name}\n";
        await File.WriteAllTextAsync(SidecarPath(artifactPath), line, Encoding.ASCII, ct).ConfigureAwait(false);
    }

    public static async Task<string> ComputeFileSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>若旁檔不存在回 null；存在則比對內容雜湊。</summary>
    public static async Task<bool?> TryVerifySidecarAsync(string artifactPath, CancellationToken ct = default)
    {
        var side = SidecarPath(artifactPath);
        if (!File.Exists(side))
            return null;
        var expected = ParseExpectedHash(await File.ReadAllTextAsync(side, ct).ConfigureAwait(false), Path.GetFileName(artifactPath));
        if (expected is null)
            return false;
        var actual = await ComputeFileSha256Async(artifactPath, ct).ConfigureAwait(false);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ParseExpectedHash(string sidecarText, string fileName)
    {
        foreach (var raw in sidecarText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length < 64)
                continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit))
                continue;
            var rest = line[64..].Trim();
            if (rest.StartsWith('*'))
                rest = rest[1..].Trim();
            if (rest.Length == 0 || rest.Equals(fileName, StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith('/' + fileName, StringComparison.OrdinalIgnoreCase)
                || rest.EndsWith('\\' + fileName, StringComparison.OrdinalIgnoreCase))
                return hash.ToLowerInvariant();
        }
        return null;
    }
}
