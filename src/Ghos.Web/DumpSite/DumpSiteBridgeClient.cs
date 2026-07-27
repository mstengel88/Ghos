using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ghos.Web.DumpSite;

public sealed class DumpSiteBridgeClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task TestAsync(
        string baseUrl,
        string sharedSecret,
        string bridgeId,
        CancellationToken cancellationToken = default)
    {
        var credentials = new DumpSiteCredentials(
            DumpSiteCredentialStore.NormalizeBaseUrl(baseUrl),
            sharedSecret.Trim(),
            DumpSiteCredentialStore.NormalizeBridgeId(bridgeId));
        if (credentials.SharedSecret.Length < 24)
        {
            throw new DumpSiteConnectionException(
                "Use a bridge secret with at least 24 characters.");
        }
        var response = await SendAsync<DumpSiteHealthResponse>(
            credentials,
            "health",
            new { },
            cancellationToken);
        if (!response.Ok)
        {
            throw new DumpSiteConnectionException(
                "The Dumpsite bridge did not confirm a healthy connection.");
        }
    }

    public async Task<IReadOnlyList<DumpSiteBridgeEntry>> ListAsync(
        DumpSiteCredentials credentials,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<DumpSiteEntriesResponse>(
            credentials,
            "operator-list",
            new { limit = Math.Clamp(limit, 1, 100) },
            cancellationToken);
        return response.Entries;
    }

    public async Task<DumpSiteBridgeEntry> ClaimAsync(
        DumpSiteCredentials credentials,
        Guid entryId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<DumpSiteEntryResponse>(
            credentials,
            "operator-claim",
            new { entryId },
            cancellationToken);
        return response.Entry ??
            throw new DumpSiteConnectionException(
                "The Dumpsite entry is no longer available.");
    }

    public Task ReleaseAsync(
        DumpSiteCredentials credentials,
        Guid entryId,
        Guid claimToken,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            credentials,
            "operator-release",
            new { entryId, claimToken },
            cancellationToken);

    public Task CompleteAsync(
        DumpSiteCredentials credentials,
        Guid entryId,
        Guid claimToken,
        string ticketNumber,
        CancellationToken cancellationToken = default) =>
        SendAsync<JsonElement>(
            credentials,
            "complete",
            new
            {
                entryId,
                claimToken,
                status = "created",
                ticketNumber
            },
            cancellationToken);

    private async Task<T> SendAsync<T>(
        DumpSiteCredentials credentials,
        string route,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{credentials.BridgeApiBaseUrl.TrimEnd('/')}/{route}");
        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                credentials.SharedSecret);
        // The Edge Function expects bridgeId and route arguments at the
        // top level, not nested beneath a payload property.
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(body, JsonOptions));
        var values = new Dictionary<string, object?>
        {
            ["bridgeId"] = credentials.BridgeId
        };
        foreach (var property in document.RootElement.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }
        request.Content = JsonContent.Create(values);

        try
        {
            using var response = await httpClient.SendAsync(
                request,
                cancellationToken);
            var content = await response.Content.ReadAsStringAsync(
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new DumpSiteConnectionException(
                    ReadError(content, response.StatusCode));
            }

            return JsonSerializer.Deserialize<T>(
                    content,
                    JsonOptions) ??
                throw new DumpSiteConnectionException(
                    "The Dumpsite bridge returned an empty response.");
        }
        catch (DumpSiteConnectionException)
        {
            throw;
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new DumpSiteConnectionException(
                "The Dumpsite bridge did not respond before the request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new DumpSiteConnectionException(
                "GHOS could not reach the Dumpsite bridge.",
                exception);
        }
    }

    private static string ReadError(
        string content,
        System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty(
                    "error",
                    out var error))
            {
                return error.GetString() ??
                    $"The Dumpsite bridge returned HTTP {(int)statusCode}.";
            }
        }
        catch (JsonException)
        {
            // Fall through to the safe status message.
        }

        return $"The Dumpsite bridge returned HTTP {(int)statusCode}.";
    }
}
