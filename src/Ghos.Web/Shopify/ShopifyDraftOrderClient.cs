using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Shopify;

public sealed record ShopifyDraftOrderCreateResult(
    string Id,
    string Name,
    string AdminUrl,
    string? InvoiceUrl);

public sealed record ShopifyDraftOrderShippingRate(
    string Handle,
    string Title,
    decimal? Price,
    string CurrencyCode);

public sealed class ShopifyDraftOrderClient(
    HttpClient httpClient,
    ShopifyDraftOrderAccessTokenProvider accessTokenProvider,
    IOptions<ShopifyOptions> options,
    ILogger<ShopifyDraftOrderClient> logger)
{
    private const string CreateMutation = """
        mutation GhosDraftOrderCreate($input: DraftOrderInput!) {
          draftOrderCreate(input: $input) {
            draftOrder {
              id
              name
              legacyResourceId
              invoiceUrl
            }
            userErrors {
              field
              message
            }
          }
        }
        """;

    private const string UpdateMutation = """
        mutation GhosDraftOrderUpdate($id: ID!, $input: DraftOrderInput!) {
          draftOrderUpdate(id: $id, input: $input) {
            draftOrder {
              id
              name
              legacyResourceId
              invoiceUrl
            }
            userErrors {
              field
              message
            }
          }
        }
        """;

    private const string CalculateMutation = """
        mutation GhosDraftOrderCalculate($input: DraftOrderInput!) {
          draftOrderCalculate(input: $input) {
            calculatedDraftOrder {
              availableShippingRates {
                handle
                title
                price {
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

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ShopifyOptions _options = options.Value;

    public async Task<ShopifyDraftOrderCreateResult> CreateAsync(
        IReadOnlyDictionary<string, object?> input,
        CancellationToken cancellationToken = default)
    {
        return await SendAsync(
            CreateMutation,
            new Dictionary<string, object?> { ["input"] = input },
            isUpdate: false,
            cancellationToken: cancellationToken);
    }

    public async Task<ShopifyDraftOrderCreateResult> UpdateAsync(
        string draftOrderId,
        IReadOnlyDictionary<string, object?> input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(draftOrderId))
        {
            throw new ArgumentException(
                "A Shopify draft-order ID is required.",
                nameof(draftOrderId));
        }

        return await SendAsync(
            UpdateMutation,
            new Dictionary<string, object?>
            {
                ["id"] = draftOrderId,
                ["input"] = input
            },
            isUpdate: true,
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ShopifyDraftOrderShippingRate>>
        CalculateShippingRatesAsync(
            IReadOnlyDictionary<string, object?> input,
            CancellationToken cancellationToken = default)
    {
        var accessToken =
            await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var payload = new GraphQlRequest(
            CalculateMutation,
            new Dictionary<string, object?> { ["input"] = input });

        using var request = CreateRequest(accessToken, payload);
        using var response = await httpClient.SendAsync(
            request,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Shopify returned HTTP {StatusCode} while calculating draft-order shipping.",
                (int)response.StatusCode);
            throw new ShopifyConnectionException(
                $"Shopify rejected the shipping calculation with HTTP {(int)response.StatusCode}.");
        }

        var graphQlResponse =
            JsonSerializer.Deserialize<GraphQlResponse>(
                responseBody,
                JsonOptions)
            ?? throw new ShopifyConnectionException(
                "Shopify returned an empty shipping-calculation response.");

        ThrowGraphQlErrors(graphQlResponse.Errors, "calculate shipping for");

        var result = graphQlResponse.Data?.DraftOrderCalculate
            ?? throw new ShopifyConnectionException(
                "Shopify did not return a shipping-calculation result.");
        ThrowUserErrors(result.UserErrors, "calculate shipping for");

        return result.CalculatedDraftOrder?.AvailableShippingRates
            .Where(rate => !string.IsNullOrWhiteSpace(rate.Handle))
            .Select(rate => new ShopifyDraftOrderShippingRate(
                rate.Handle,
                string.IsNullOrWhiteSpace(rate.Title)
                    ? "Delivery"
                    : rate.Title,
                ParseMoney(rate.Price?.Amount),
                string.IsNullOrWhiteSpace(rate.Price?.CurrencyCode)
                    ? "USD"
                    : rate.Price.CurrencyCode))
            .ToList()
            ?? [];
    }

    private async Task<ShopifyDraftOrderCreateResult> SendAsync(
        string mutation,
        IReadOnlyDictionary<string, object?> variables,
        bool isUpdate,
        CancellationToken cancellationToken)
    {
        var operation = isUpdate ? "update" : "create";
        var accessToken =
            await accessTokenProvider.GetAccessTokenAsync(cancellationToken);
        var payload = new GraphQlRequest(mutation, variables);

        using var request = CreateRequest(accessToken, payload);

        using var response = await httpClient.SendAsync(
            request,
            cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Shopify returned HTTP {StatusCode} while {Operation} a draft order.",
                (int)response.StatusCode,
                isUpdate ? "updating" : "creating");
            throw new ShopifyConnectionException(
                $"Shopify rejected the draft-order request with HTTP {(int)response.StatusCode}.");
        }

        var graphQlResponse =
            JsonSerializer.Deserialize<GraphQlResponse>(
                responseBody,
                JsonOptions)
            ?? throw new ShopifyConnectionException(
                "Shopify returned an empty draft-order response.");

        ThrowGraphQlErrors(graphQlResponse.Errors, operation);

        var result = (isUpdate
            ? graphQlResponse.Data?.DraftOrderUpdate
            : graphQlResponse.Data?.DraftOrderCreate)
            ?? throw new ShopifyConnectionException(
                "Shopify did not return a draft-order result.");

        ThrowUserErrors(result.UserErrors, operation);

        var draftOrder = result.DraftOrder
            ?? throw new ShopifyConnectionException(
                "Shopify did not return the saved draft order.");
        var storeName = _options.StoreDomain
            .Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var adminUrl =
            $"https://admin.shopify.com/store/{storeName}/draft_orders/{draftOrder.LegacyResourceId}";

        return new ShopifyDraftOrderCreateResult(
            draftOrder.Id,
            draftOrder.Name,
            adminUrl,
            draftOrder.InvoiceUrl);
    }

    private HttpRequestMessage CreateRequest(
        string accessToken,
        GraphQlRequest payload)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://{_options.StoreDomain}/admin/api/{_options.ApiVersion}/graphql.json");
        request.Headers.TryAddWithoutValidation(
            "X-Shopify-Access-Token",
            accessToken);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        return request;
    }

    private void ThrowGraphQlErrors(
        IReadOnlyCollection<GraphQlError> errors,
        string operation)
    {
        if (errors.Count == 0)
        {
            return;
        }

        var message = string.Join(
            "; ",
            errors.Select(error => error.Message));
        logger.LogWarning(
            "Shopify draft-order GraphQL errors: {Errors}",
            message);
        throw new ShopifyConnectionException(
            $"Shopify could not {operation} the draft order: {message}");
    }

    private static void ThrowUserErrors(
        IReadOnlyCollection<UserError> errors,
        string operation)
    {
        if (errors.Count == 0)
        {
            return;
        }

        var message = string.Join(
            "; ",
            errors.Select(error =>
                string.IsNullOrWhiteSpace(error.FieldText)
                    ? error.Message
                    : $"{error.FieldText}: {error.Message}"));
        throw new ShopifyConnectionException(
            $"Shopify could not {operation} the draft order: {message}");
    }

    private static decimal? ParseMoney(string? amount) =>
        decimal.TryParse(
            amount,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : null;

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
        [JsonPropertyName("draftOrderCreate")]
        public DraftOrderCreatePayload? DraftOrderCreate { get; init; }

        [JsonPropertyName("draftOrderUpdate")]
        public DraftOrderCreatePayload? DraftOrderUpdate { get; init; }

        [JsonPropertyName("draftOrderCalculate")]
        public DraftOrderCalculatePayload? DraftOrderCalculate { get; init; }
    }

    private sealed class DraftOrderCreatePayload
    {
        [JsonPropertyName("draftOrder")]
        public DraftOrderNode? DraftOrder { get; init; }

        [JsonPropertyName("userErrors")]
        public List<UserError> UserErrors { get; init; } = [];
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
        [JsonPropertyName("availableShippingRates")]
        public List<ShippingRateNode> AvailableShippingRates { get; init; } = [];
    }

    private sealed class ShippingRateNode
    {
        [JsonPropertyName("handle")]
        public string Handle { get; init; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; init; } = string.Empty;

        [JsonPropertyName("price")]
        public MoneyNode? Price { get; init; }
    }

    private sealed class MoneyNode
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; init; }

        [JsonPropertyName("currencyCode")]
        public string? CurrencyCode { get; init; }
    }

    private sealed class DraftOrderNode
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("legacyResourceId")]
        public string LegacyResourceId { get; init; } = string.Empty;

        [JsonPropertyName("invoiceUrl")]
        public string? InvoiceUrl { get; init; }
    }

    private sealed class GraphQlError
    {
        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;
    }

    private sealed class UserError
    {
        [JsonPropertyName("field")]
        public List<string>? Field { get; init; }

        [JsonPropertyName("message")]
        public string Message { get; init; } = string.Empty;

        public string FieldText =>
            Field is null ? string.Empty : string.Join(".", Field);
    }
}
