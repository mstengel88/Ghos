using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghos.Web.ProjectTools;

public sealed record QuoteDeliveryItem(
    string? Sku,
    decimal Quantity,
    string? PickupVendor,
    decimal? UnitWeightPounds = null);

public sealed record QuoteDeliveryRequest(
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string Country,
    decimal? RatePerMinute,
    IReadOnlyList<QuoteDeliveryItem> Items);

public sealed record QuoteDeliveryResult(
    decimal Amount,
    string ServiceName,
    string Description,
    string Eta,
    string Summary,
    string SourceBreakdownJson,
    bool IsOutsideDeliveryArea = false,
    decimal? OutsideDeliveryMiles = null);

/// <summary>
/// Uses the same delivery calculator endpoint that Shopify invokes for checkout
/// carrier rates. GHOS deliberately does not keep a second copy of the pricing
/// rules because duplicated origin, capacity, and rate logic can drift.
/// </summary>
public sealed class QuoteDeliveryService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<QuoteDeliveryService> logger)
{
    private const string DefaultCalculatorUrl =
        "http://ghos-shopify-bridge:3000/api/shipping-estimate";

    public async Task<QuoteDeliveryResult> CalculateAsync(
        QuoteDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.AddressLine1) ||
            string.IsNullOrWhiteSpace(request.City) ||
            string.IsNullOrWhiteSpace(request.State) ||
            string.IsNullOrWhiteSpace(request.PostalCode))
        {
            return Unavailable("Missing destination address");
        }

        var calculatorUrl =
            configuration["Quote:ShippingCalculatorUrl"] ??
            DefaultCalculatorUrl;
        var shop =
            configuration["Shopify:StoreDomain"] ??
            "darfaz-2e.myshopify.com";

        var payload = new ShippingEstimateRequest(
            shop,
            new ShippingAddress(
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.State,
                request.PostalCode,
                string.IsNullOrWhiteSpace(request.Country)
                    ? "US"
                    : request.Country),
            request.Items
                .Where(item => item.Quantity > 0)
                .Select(item => new ShippingLine(
                    item.Sku,
                    item.Quantity,
                    item.UnitWeightPounds is > 0
                        ? decimal.Round(
                            item.UnitWeightPounds.Value * 453.59237m,
                            3)
                        : 0m,
                    item.PickupVendor,
                    true))
                .ToList());

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                calculatorUrl,
                payload,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                logger.LogWarning(
                    "Shared Shopify delivery calculator returned {StatusCode}: {Response}",
                    (int)response.StatusCode,
                    responseBody);
                return Unavailable(
                    "Shopify delivery calculator is temporarily unavailable");
            }

            var quote = await response.Content
                .ReadFromJsonAsync<ShippingEstimateResponse>(
                    cancellationToken: cancellationToken);
            if (quote is null)
            {
                return Unavailable(
                    "Shopify delivery calculator returned an empty response");
            }

            var amount = decimal.Round(quote.Cents / 100m, 2);
            var sourceBreakdown = JsonSerializer.Serialize(new[]
            {
                new
                {
                    source = "Shopify delivery calculator",
                    calculator = quote.Calculator ?? "shopify-local-delivery",
                    pickupVendors = request.Items
                        .Select(item => item.PickupVendor)
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase),
                    amount
                }
            });

            return new QuoteDeliveryResult(
                amount,
                quote.ServiceName ?? "Green Hills Delivery",
                quote.Description ?? "Standard delivery pricing",
                quote.Eta ?? "2–4 business days",
                quote.Summary ?? $"Shipping: {amount:C2}",
                sourceBreakdown,
                quote.OutsideDeliveryArea,
                quote.OutsideDeliveryMiles);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Unable to retrieve a quote from the shared Shopify delivery calculator.");
            return Unavailable(
                "Shopify delivery calculator is temporarily unavailable");
        }
    }

    private static QuoteDeliveryResult Unavailable(string message) =>
        new(0m, "Delivery Unavailable", message, "Unavailable", message, "[]");

    private sealed record ShippingEstimateRequest(
        [property: JsonPropertyName("shop")] string Shop,
        [property: JsonPropertyName("shippingAddress")] ShippingAddress ShippingAddress,
        [property: JsonPropertyName("lines")] IReadOnlyList<ShippingLine> Lines);

    private sealed record ShippingAddress(
        [property: JsonPropertyName("address1")] string Address1,
        [property: JsonPropertyName("address2")] string? Address2,
        [property: JsonPropertyName("city")] string City,
        [property: JsonPropertyName("provinceCode")] string ProvinceCode,
        [property: JsonPropertyName("zip")] string Zip,
        [property: JsonPropertyName("countryCode")] string CountryCode);

    private sealed record ShippingLine(
        [property: JsonPropertyName("sku")] string? Sku,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("grams")] decimal Grams,
        [property: JsonPropertyName("pickupVendor")] string? PickupVendor,
        [property: JsonPropertyName("requiresShipping")] bool RequiresShipping);

    private sealed class ShippingEstimateResponse
    {
        [JsonPropertyName("cents")]
        public long Cents { get; init; }

        [JsonPropertyName("serviceName")]
        public string? ServiceName { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("eta")]
        public string? Eta { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("outsideDeliveryArea")]
        public bool OutsideDeliveryArea { get; init; }

        [JsonPropertyName("outsideDeliveryMiles")]
        public decimal? OutsideDeliveryMiles { get; init; }

        [JsonPropertyName("calculator")]
        public string? Calculator { get; init; }
    }
}
