using AiProject.Console.Core.Runtime;

namespace AiProject.Console.App.Services;

public sealed class CompanyBaseAddressHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = ConsoleSettingsStore.GetCompanyBaseUrl();
        if (!string.IsNullOrWhiteSpace(url) && request.RequestUri is { IsAbsoluteUri: false })
            request.RequestUri = new Uri(new Uri(url.TrimEnd('/') + "/"), request.RequestUri);
        return base.SendAsync(request, cancellationToken);
    }
}
