using System.Net.Http.Headers;
using System.Net.Http.Json;
using AiProject.Company.Contracts;

namespace AiProject.Console.CompanyClient;

public interface ICompanyPlatformClient
{
    Task<TimesheetUploadResponse> UploadAsync(TimesheetUploadRequest request, string bearerToken, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssignmentDto>> AssignmentsAsync(string bearerToken, CancellationToken cancellationToken = default);
    Task<PayslipDto?> PayslipAsync(string bearerToken, CancellationToken cancellationToken = default);
}

public sealed class CompanyPlatformClient : ICompanyPlatformClient
{
    readonly HttpClient _http;

    public CompanyPlatformClient(HttpClient http) => _http = http;

    public async Task<TimesheetUploadResponse> UploadAsync(TimesheetUploadRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/timesheets/upload")
        {
            Content = JsonContent.Create(request),
        };
        Apply(req, bearerToken);
        var response = await SendWithRetry(req, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
            throw new CompanyPlatformException(error?.Code ?? "unmatched_person", error?.Message ?? "待歸戶");
        }
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
            throw new CompanyPlatformException(error?.Code ?? "upload_failed", error?.Message ?? "送到公司失敗，可以重試。本機 work-hours.json 仍在。");
        }
        return await response.Content.ReadFromJsonAsync<TimesheetUploadResponse>(cancellationToken)
            ?? throw new CompanyPlatformException("upload_failed", "伺服器沒有回傳上傳結果。");
    }

    public async Task<IReadOnlyList<AssignmentDto>> AssignmentsAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/v1/me/assignments");
        Apply(req, bearerToken);
        var response = await SendWithRetry(req, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<AssignmentDto>>(cancellationToken) ?? [];
    }

    public async Task<PayslipDto?> PayslipAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/v1/me/payslip");
        Apply(req, bearerToken);
        var response = await SendWithRetry(req, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PayslipDto>(cancellationToken);
    }

    static void Apply(HttpRequestMessage req, string bearerToken)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        req.Headers.TryAddWithoutValidation(CompanyApiVersions.Header, CompanyApiVersions.Current);
    }

    async Task<HttpResponseMessage> SendWithRetry(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var req = await Clone(template, cancellationToken);
            last = await _http.SendAsync(req, cancellationToken);
            if ((int)last.StatusCode < 500 && last.StatusCode != System.Net.HttpStatusCode.RequestTimeout)
                return last;
            await Task.Delay(200 * (attempt + 1), cancellationToken);
        }
        return last!;
    }

    static async Task<HttpRequestMessage> Clone(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(template.Method, template.RequestUri);
        foreach (var header in template.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (template.Content is not null)
        {
            var bytes = await template.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in template.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }
}

public sealed class CompanyPlatformException : Exception
{
    public string Code { get; }
    public CompanyPlatformException(string code, string message) : base(message) => Code = code;
}
