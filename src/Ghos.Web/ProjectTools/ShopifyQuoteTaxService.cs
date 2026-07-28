using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ghos.Web.Data;
using Ghos.Web.Shopify;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.ProjectTools;

public sealed class ShopifyQuoteTaxService(
    HttpClient httpClient,
    ShopifyDraftOrderAccessTokenProvider accessTokenProvider,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    QuoteTaxCalculator fallbackCalculator,
    IOptions<ShopifyOptions> options,
    ILogger<ShopifyQuoteTaxService> logger)
{
    private const decimal SampleTaxableAmount = 100m;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ShopifyOptions _options = options.Value;

    public async Task<QuoteTaxRateMatch> ResolveAsync(
        QuoteTaxAddress address,
        CancellationToken cancellationToken = default)
    {
        var fallback = fallbackCalculator.Resolve(address);
        var normalized = NormalizedAddress.Create(address);
        if (!normalized.CanCalculate)
        {
            return fallback;
        }

        var cacheKey = CreateCacheKey(normalized);
        var cached = await ReadCacheAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var rate = await CalculateWithShopifyAsync(
                normalized,
                cancellationToken);
            if (rate is null)
            {
                return fallback;
            }

            await WriteCacheAsync(
                cacheKey,
                normalized,
                rate.Value,
                cancellationToken);
            return new QuoteTaxRateMatch(
                rate.Value,
                "Shopify tax",
                true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Shopify tax lookup failed; GHOS is using the configured fallback tax rule.");
            return fallback;
        }
    }

    private async Task<decimal?> CalculateWithShopifyAsync(
        NormalizedAddress address,
        CancellationToken cancellationToken)
    {
        var accessToken =
            await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var payload = new GraphQlRequest(
            CalculateMutation,
            new Dictionary<string, object?>
            {
                ["input"] = new Dictionary<string, object?>
                {
                    ["lineItems"] = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["title"] = "Quote Tax Rate Check",
                            ["quantity"] = 1,
                            ["originalUnitPriceWithCurrency"] =
                                new Dictionary<string, object?>
                                {
                                    ["amount"] =
                                        SampleTaxableAmount.ToString("0.00"),
                                    ["currencyCode"] = "USD"
                                },
                            ["requiresShipping"] = true,
                            ["taxable"] = true
                        }
                    },
                    ["shippingAddress"] = new Dictionary<string, object?>
                    {
                        ["address1"] = address.AddressLine1,
                        ["address2"] = address.AddressLine2,
                        ["city"] = address.City,
                        ["province"] = address.State,
                        ["zip"] = address.PostalCode,
                        ["country"] = address.Country
                    }
                }
            });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{_options.StoreDomain}/admin/api/{_options.ApiVersion}/graphql.json");
        request.Headers.TryAddWithoutValidation(
            "X-Shopify-Access-Token",
            accessToken);
        request.Content = JsonContent.Create(
            payload,
            options: JsonOptions);

        using var response = await httpClient.SendAsync(
            request,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ShopifyConnectionException(
                $"Shopify tax lookup returned HTTP {(int)response.StatusCode}.");
        }

        var graphQlResponse =
            JsonSerializer.Deserialize<GraphQlResponse>(
                responseBody,
                JsonOptions)
            ?? throw new ShopifyConnectionException(
                "Shopify returned an empty tax response.");
        if (graphQlResponse.Errors.Count > 0)
        {
            throw new ShopifyConnectionException(string.Join(
                "; ",
                graphQlResponse.Errors.Select(error => error.Message)));
        }

        var result = graphQlResponse.Data?.DraftOrderCalculate
            ?? throw new ShopifyConnectionException(
                "Shopify did not return a draft-order tax calculation.");
        if (result.UserErrors.Count > 0)
        {
            throw new ShopifyConnectionException(string.Join(
                "; ",
                result.UserErrors.Select(error => error.Message)));
        }

        var rawTax =
            result.CalculatedDraftOrder?.TotalTaxSet?.ShopMoney?.Amount;
        if (!decimal.TryParse(
                rawTax,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var taxAmount) ||
            taxAmount < 0m)
        {
            return null;
        }

        return Math.Round(
            taxAmount / SampleTaxableAmount,
            6,
            MidpointRounding.AwayFromZero);
    }

    private async Task<QuoteTaxRateMatch?> ReadCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var cached = await dbContext.QuoteTaxRateCache
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.CacheKey == cacheKey &&
                    item.ExpiresAtUtc > now,
                cancellationToken);
        return cached is null
            ? null
            : new QuoteTaxRateMatch(
                cached.Rate,
                cached.Label,
                true);
    }

    private async Task WriteCacheAsync(
        string cacheKey,
        NormalizedAddress address,
        decimal rate,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cached = await dbContext.QuoteTaxRateCache
            .SingleOrDefaultAsync(
                item => item.CacheKey == cacheKey,
                cancellationToken);
        if (cached is null)
        {
            cached = new QuoteTaxRateCache { CacheKey = cacheKey };
            dbContext.QuoteTaxRateCache.Add(cached);
        }

        var now = DateTime.UtcNow;
        cached.AddressLine1 = address.AddressLine1;
        cached.City = address.City;
        cached.State = address.State;
        cached.PostalCode = address.PostalCode;
        cached.Country = address.Country;
        cached.Rate = rate;
        cached.Label = "Shopify tax";
        cached.Source = "shopify";
        cached.SampleTaxableAmount = SampleTaxableAmount;
        cached.ShopifyTotalTax = Math.Round(
            rate * SampleTaxableAmount,
            2,
            MidpointRounding.AwayFromZero);
        cached.CalculatedAtUtc = now;
        cached.ExpiresAtUtc = now.Add(CacheDuration);
        cached.UpdatedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // Concurrent requests can calculate the same new address before
            // either insert commits. The calculated rate is still valid.
            logger.LogDebug(
                exception,
                "A concurrent Shopify tax cache write won the race.");
        }
    }

    private static string CreateCacheKey(NormalizedAddress address)
    {
        var value = string.Join(
            '|',
            address.Country.ToLowerInvariant(),
            address.State.ToLowerInvariant(),
            address.PostalCode.ToUpperInvariant(),
            address.City.ToLowerInvariant(),
            address.AddressLine1.ToLowerInvariant());
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private const string CalculateMutation = """
        mutation CalculateQuoteTax($input: DraftOrderInput!) {
          draftOrderCalculate(input: $input) {
            calculatedDraftOrder {
              totalTaxSet {
                shopMoney {
                  amount
                  currencyCode
                }
              }
            }
            userErrors {
              field
              message
            }
          }
        }
        """;

    private sealed record GraphQlRequest(
        string Query,
        IReadOnlyDictionary<string, object?> Variables);

    private sealed class GraphQlResponse
    {
        [JsonPropertyName("data")]
        public GraphQlData? Data { get; init; }

        [JsonPropertyName("errors")]
        public List<GraphQlError> Errors { get; init; } = [];
    }

    private sealed class GraphQlData
    {
        [JsonPropertyName("draftOrderCalculate")]
        public DraftOrderCalculatePayload? DraftOrderCalculate { get; init; }
    }

    private sealed class DraftOrderCalculatePayload
    {
        [JsonPropertyName("calculatedDraftOrder")]
        public CalculatedDraftOrder? CalculatedDraftOrder { get; init; }

        [JsonPropertyName("userErrors")]
        public List<UserError> UserErrors { get; init; } = [];
    }

    private sealed class CalculatedDraftOrder
    {
        [JsonPropertyName("totalTaxSet")]
        public MoneyBag? TotalTaxSet { get; init; }
    }

    private sealed class MoneyBag
    {
        [JsonPropertyName("shopMoney")]
        public Money? ShopMoney { get; init; }
    }

    private sealed class Money
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; init; }
    }

    private sealed class GraphQlError
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    private sealed class UserError
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    private sealed record NormalizedAddress(
        string AddressLine1,
        string AddressLine2,
        string City,
        string State,
        string PostalCode,
        string Country)
    {
        public bool CanCalculate =>
            City.Length > 0 &&
            State.Length > 0 &&
            PostalCode.Length > 0;

        public static NormalizedAddress Create(QuoteTaxAddress address) =>
            new(
                Normalize(address.AddressLine1),
                Normalize(address.AddressLine2),
                Normalize(address.City).Split(',')[0],
                Normalize(address.State, "WI"),
                NormalizePostalCode(address.PostalCode),
                Normalize(address.Country, "US"));

        private static string Normalize(
            string? value,
            string fallback = "") =>
            string.Join(
                ' ',
                (value ?? fallback)
                    .Trim()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries));

        private static string NormalizePostalCode(string? value) =>
            string.Concat(
                (value ?? string.Empty)
                    .Trim()
                    .ToUpperInvariant()
                    .Where(character => !char.IsWhiteSpace(character)));
    }
}
