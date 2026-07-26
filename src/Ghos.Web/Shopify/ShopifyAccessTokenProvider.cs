using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Shopify;

public sealed class ShopifyAccessTokenProvider(
    HttpClient httpClient,
    ShopifyCredentialStore credentialStore,
    IOptions<ShopifyOptions> options,
    ILogger<ShopifyAccessTokenProvider> logger)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ShopifyOptions _options = options.Value;
    private CachedAccessToken? _cachedToken;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedToken is not null && _cachedToken.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            return _cachedToken.Value;
        }

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            if (_cachedToken is not null && _cachedToken.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return _cachedToken.Value;
            }

            var credentials = await credentialStore.GetAsync(cancellationToken)
                ?? throw new ShopifyConnectionException(
                    "Shopify credentials have not been saved in GHOS.");
            _cachedToken = await RequestAccessTokenAsync(credentials, cancellationToken);
            return _cachedToken.Value;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task ValidateCredentialsAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default)
    {
        var credentials = new ShopifyCredentials(clientId.Trim(), clientSecret.Trim());
        var validatedToken = await RequestAccessTokenAsync(credentials, cancellationToken);

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            _cachedToken = validatedToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Invalidate() => _cachedToken = null;

    private async Task<CachedAccessToken> RequestAccessTokenAsync(
        ShopifyCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{_options.StoreDomain}/admin/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = credentials.ClientId,
                    ["client_secret"] = credentials.ClientSecret
                })
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Shopify token exchange returned HTTP {StatusCode}.",
                (int)response.StatusCode);
            throw new ShopifyConnectionException(
                $"Shopify rejected the client credentials with HTTP {(int)response.StatusCode}. Confirm the app is installed and the client ID and secret are correct.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken: cancellationToken)
            ?? throw new ShopifyConnectionException(
                "Shopify returned an empty token response.");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            throw new ShopifyConnectionException(
                "Shopify did not return an access token.");
        }

        var grantedScopes = tokenResponse.Scope
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!grantedScopes.Contains("read_products"))
        {
            throw new ShopifyConnectionException(
                "The Shopify app is connected but does not have the required read_products scope.");
        }

        return new CachedAccessToken(
            tokenResponse.AccessToken,
            DateTime.UtcNow.AddSeconds(Math.Max(tokenResponse.ExpiresIn, 300)));
    }

    private sealed record CachedAccessToken(string Value, DateTime ExpiresAtUtc);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("scope")]
        public string Scope { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
