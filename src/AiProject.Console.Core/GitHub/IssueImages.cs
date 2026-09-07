using System.Net;
using System.Net.Http.Headers;
using AiProject.Console.Core;

namespace AiProject.Console.Core.GitHub;

/// <summary>
/// GitHub Issue 附件圖要帶登入才能抓；WebView 沒有 GitHub cookie，所以改成本機 data URI。
/// </summary>
public static class IssueImages
{
    public const int MaxImages = 8;
    public const int MaxBytes = 8 * 1024 * 1024;

    public static async Task<IReadOnlyDictionary<string, string>> ToDataUrisAsync(
        IEnumerable<string> urls,
        string? cwd,
        GithubConfig? cfg,
        CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var list = urls
            .Where(u => IssueMarkdown.IsTrustedImageHost(u, cfg?.ResolvedHost()))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxImages)
            .ToList();
        if (list.Count == 0)
            return map;
        var token = await ReadTokenAsync(cwd, cfg, ct).ConfigureAwait(false);
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AutomaticDecompression = DecompressionMethods.All,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.SettingsDirName}/{AppInfo.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("image/*,*/*;q=0.8");
        foreach (var url in list)
        {
            ct.ThrowIfCancellationRequested();
            var data = await TryDownloadAsync(http, url, token, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(data))
                map[url] = data;
        }
        return map;
    }

    public static string? TryDataUri(byte[] bytes, string? contentType)
    {
        if (bytes.Length == 0 || bytes.Length > MaxBytes)
            return null;
        var mime = GuessMime(bytes) ?? ImageContentType(contentType);
        if (mime is null)
            return null;
        return "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
    }

    public static string? GuessMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 6
            && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
            return "image/gif";
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "image/webp";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return "image/bmp";
        return null;
    }

    internal static async Task<string?> TryDownloadAsync(
        HttpClient http,
        string url,
        string? token,
        CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.ParseAdd("application/octet-stream");
            req.Headers.Accept.ParseAdd("image/*");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return TryDataUri(bytes, resp.Content.Headers.ContentType?.MediaType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    static string? ImageContentType(string? contentType)
    {
        var raw = (contentType ?? "").Trim().ToLowerInvariant();
        var semi = raw.IndexOf(';');
        if (semi >= 0)
            raw = raw[..semi].Trim();
        return raw switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" => "image/jpeg",
            "image/gif" => "image/gif",
            "image/webp" => "image/webp",
            "image/bmp" => "image/bmp",
            _ => null,
        };
    }

    static async Task<string?> ReadTokenAsync(string? cwd, GithubConfig? cfg, CancellationToken ct)
    {
        try
        {
            var (code, stdout, _) = await GhCli.RunCaptureAsync(
                ["auth", "token"], cwd, cfg, 15_000, ct).ConfigureAwait(false);
            var token = stdout.Trim();
            return code == 0 && token.Length > 0 ? token : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
