using System.Net.Http.Json;

namespace Ghos.Web.Dispatch;

public sealed class DispatchIntegrationClient(HttpClient httpClient)
{
    public async Task<DispatchExportEnvelope> FetchAsync(
        string baseUrl,
        string integrationSecret,
        string? updatedAfter = null,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var normalizedUrl = DispatchCredentialStore.NormalizeBaseUrl(baseUrl);
        var endpoint = new UriBuilder($"{normalizedUrl}/api/ghos-export");
        var query = $"limit={Math.Clamp(limit, 1, 1000)}";
        if (!string.IsNullOrWhiteSpace(updatedAfter))
        {
            query += $"&updatedAfter={Uri.EscapeDataString(updatedAfter)}";
        }

        endpoint.Query = query;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            endpoint.Uri);
        var normalizedSecret = integrationSecret.Trim();
        request.Headers.Add("x-ghos-secret", normalizedSecret);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            throw new DispatchConnectionException(
                "GHOS could not reach the dispatch app. Check its address and network availability.",
                exception);
        }

        using (response)
        {
            DispatchExportEnvelope? payload = null;
            try
            {
                payload = await response.Content
                    .ReadFromJsonAsync<DispatchExportEnvelope>(
                        cancellationToken: cancellationToken);
            }
            catch
            {
                // The status-specific message below is safer than returning
                // arbitrary upstream HTML or proxy error content.
            }

            if (!response.IsSuccessStatusCode || payload is null || !payload.Ok)
            {
                var message = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized =>
                        "The dispatch app rejected the integration secret. " +
                        $"GHOS sent fingerprint {DispatchCredentialStore.CreateFingerprint(normalizedSecret)}; " +
                        "the running Dispatch V2 container is using a different GHOS_INTEGRATION_SECRET.",
                    System.Net.HttpStatusCode.NotFound =>
                        "The dispatch app does not have the GHOS export endpoint deployed yet.",
                    _ => payload?.Message ??
                        $"Dispatch returned HTTP {(int)response.StatusCode}."
                };
                throw new DispatchConnectionException(message);
            }

            if (payload.Version != "1")
            {
                throw new DispatchConnectionException(
                    $"Dispatch returned unsupported export version '{payload.Version}'.");
            }

            return payload;
        }
    }
}
