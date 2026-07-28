using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Ghos.Web.Shopify;

public sealed class ShopifyAdminClient(
    HttpClient httpClient,
    ShopifyAccessTokenProvider accessTokenProvider,
    IOptions<ShopifyOptions> options,
    ILogger<ShopifyAdminClient> logger)
{
    private const string ProductsQuery = """
        query GhosProducts($after: String) {
          products(first: 75, after: $after, sortKey: ID) {
            edges {
              cursor
              node {
                id
                title
                handle
                status
                descriptionHtml
                vendor
                productType
                unitLabel: metafield(namespace: "green_hills", key: "price_unit_label") {
                  value
                }
                legacyUnitLabel: metafield(namespace: "$app", key: "price_unit_label") {
                  value
                }
                projectCalculatorType: metafield(namespace: "custom", key: "project_calculator_type") { value }
                coveragePerOrderUnitSqFt: metafield(namespace: "custom", key: "coverage_per_order_unit_sq_ft") { value }
                calculatorOrderUnitLabel: metafield(namespace: "custom", key: "calculator_order_unit_label") { value }
                piecesPerOrderUnit: metafield(namespace: "custom", key: "pieces_per_order_unit") { value }
                calculatorUnitLengthInches: metafield(namespace: "custom", key: "unit_length_inches") { value }
                calculatorUnitHeightInches: metafield(namespace: "custom", key: "unit_height_inches") { value }
                layersPerPallet: metafield(namespace: "custom", key: "layers_per_pallet") { value }
                squareFeetPerLayer: metafield(namespace: "custom", key: "square_feet_per_layer") { value }
                palletWeightLbs: metafield(namespace: "custom", key: "pallet_weight_lbs") { value }
                tags
                createdAt
                updatedAt
                publishedAt
                seo {
                  title
                  description
                }
                featuredMedia {
                  ... on MediaImage {
                    image {
                      url
                      altText
                    }
                  }
                }
                collections(first: 20) {
                  nodes {
                    id
                    title
                    handle
                  }
                }
                variants(first: 100) {
                  nodes {
                    id
                    title
                    sku
                    barcode
                    price
                    compareAtPrice
                    availableForSale
                    image {
                      url
                    }
                    coveragePerOrderUnitSqFt: metafield(namespace: "custom", key: "coverage_per_order_unit_sq_ft") { value }
                    calculatorOrderUnitLabel: metafield(namespace: "custom", key: "calculator_order_unit_label") { value }
                    piecesPerOrderUnit: metafield(namespace: "custom", key: "pieces_per_order_unit") { value }
                    calculatorUnitLengthInches: metafield(namespace: "custom", key: "unit_length_inches") { value }
                    calculatorUnitHeightInches: metafield(namespace: "custom", key: "unit_height_inches") { value }
                    layersPerPallet: metafield(namespace: "custom", key: "layers_per_pallet") { value }
                    squareFeetPerLayer: metafield(namespace: "custom", key: "square_feet_per_layer") { value }
                    palletWeightLbs: metafield(namespace: "custom", key: "pallet_weight_lbs") { value }
                  }
                }
              }
            }
            pageInfo {
              hasNextPage
            }
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ShopifyOptions _options = options.Value;

    public string StoreDomain => _options.StoreDomain;

    public string ApiVersion => _options.ApiVersion;

    public string StorefrontUrl => _options.StorefrontUrl;

    public async Task<IReadOnlyList<ShopifyProductSnapshot>> GetProductsAsync(
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration();
        var accessToken = await accessTokenProvider.GetAccessTokenAsync(cancellationToken);

        var products = new List<ShopifyProductSnapshot>();
        string? cursor = null;

        do
        {
            var payload = new GraphQlRequest(
                ProductsQuery,
                new Dictionary<string, object?> { ["after"] = cursor });
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://{_options.StoreDomain}/admin/api/{_options.ApiVersion}/graphql.json");
            request.Headers.TryAddWithoutValidation(
                "X-Shopify-Access-Token",
                accessToken);
            request.Content = JsonContent.Create(payload, options: JsonOptions);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Shopify returned HTTP {StatusCode} while retrieving products.",
                    (int)response.StatusCode);
                throw new ShopifyConnectionException(
                    $"Shopify rejected the product request with HTTP {(int)response.StatusCode}. Check the store connection and read_products permission.");
            }

            var graphQlResponse = JsonSerializer.Deserialize<GraphQlResponse>(responseBody, JsonOptions)
                ?? throw new ShopifyConnectionException("Shopify returned an empty response.");

            if (graphQlResponse.Errors is { Count: > 0 })
            {
                var errorMessage = string.Join(
                    "; ",
                    graphQlResponse.Errors.Select(error => error.Message));
                logger.LogWarning(
                    "Shopify GraphQL rejected the product query: {ErrorMessage}",
                    errorMessage);
                throw new ShopifyConnectionException($"Shopify could not return products: {errorMessage}");
            }

            var connection = graphQlResponse.Data?.Products
                ?? throw new ShopifyConnectionException("Shopify did not include a product collection in its response.");

            foreach (var edge in connection.Edges)
            {
                products.Add(MapProduct(edge.Node));
            }

            cursor = connection.PageInfo.HasNextPage
                ? connection.Edges.LastOrDefault()?.Cursor
                : null;

            if (connection.PageInfo.HasNextPage && string.IsNullOrWhiteSpace(cursor))
            {
                throw new ShopifyConnectionException("Shopify indicated another page but did not provide a cursor.");
            }
        }
        while (cursor is not null);

        return products;
    }

    private void ValidateConfiguration()
    {
        if (!_options.StoreDomain.EndsWith(".myshopify.com", StringComparison.OrdinalIgnoreCase) ||
            _options.StoreDomain.Contains('/') ||
            Uri.CheckHostName(_options.StoreDomain) != UriHostNameType.Dns)
        {
            throw new ShopifyConnectionException(
                "The Shopify store domain must be a valid myshopify.com hostname.");
        }
    }

    private static ShopifyProductSnapshot MapProduct(ProductNode node)
    {
        var variants = node.Variants.Nodes
            .Select(variant => new ShopifyVariantSnapshot(
                variant.Id,
                variant.Title,
                NullIfWhiteSpace(variant.Sku),
                NullIfWhiteSpace(variant.Barcode),
                ParseMoney(variant.Price),
                ParseNullableMoney(variant.CompareAtPrice),
                variant.Image?.Url,
                variant.AvailableForSale,
                ParseNullableDecimal(variant.CoveragePerOrderUnitSqFt?.Value),
                NullIfWhiteSpace(variant.CalculatorOrderUnitLabel?.Value),
                ParseNullableInt(variant.PiecesPerOrderUnit?.Value),
                ParseNullableDecimal(variant.CalculatorUnitLengthInches?.Value),
                ParseNullableDecimal(variant.CalculatorUnitHeightInches?.Value),
                ParseNullableInt(variant.LayersPerPallet?.Value),
                ParseNullableDecimal(variant.SquareFeetPerLayer?.Value),
                ParseNullableInt(variant.PalletWeightLbs?.Value)))
            .ToList();

        var collections = node.Collections.Nodes
            .Select(collection => new ShopifyCollectionSnapshot(
                collection.Id,
                collection.Title,
                collection.Handle))
            .ToList();

        return new ShopifyProductSnapshot(
            node.Id,
            node.Title,
            node.Handle,
            node.Status,
            node.DescriptionHtml,
            NullIfWhiteSpace(node.Vendor),
            NullIfWhiteSpace(node.ProductType),
            NullIfWhiteSpace(
                node.UnitLabel?.Value ?? node.LegacyUnitLabel?.Value),
            node.Tags,
            NullIfWhiteSpace(node.Seo?.Title),
            NullIfWhiteSpace(node.Seo?.Description),
            node.FeaturedMedia?.Image?.Url,
            NullIfWhiteSpace(node.FeaturedMedia?.Image?.AltText),
            node.CreatedAt,
            node.UpdatedAt,
            node.PublishedAt,
            new ShopifyProjectCalculatorSnapshot(
                NullIfWhiteSpace(node.ProjectCalculatorType?.Value),
                ParseNullableDecimal(node.CoveragePerOrderUnitSqFt?.Value),
                NullIfWhiteSpace(node.CalculatorOrderUnitLabel?.Value),
                ParseNullableInt(node.PiecesPerOrderUnit?.Value),
                ParseNullableDecimal(node.CalculatorUnitLengthInches?.Value),
                ParseNullableDecimal(node.CalculatorUnitHeightInches?.Value),
                ParseNullableInt(node.LayersPerPallet?.Value),
                ParseNullableDecimal(node.SquareFeetPerLayer?.Value),
                ParseNullableInt(node.PalletWeightLbs?.Value)),
            collections,
            variants);
    }

    private static decimal ParseMoney(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static decimal? ParseNullableMoney(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseMoney(value);

    private static decimal? ParseNullableDecimal(string? value) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : null;

    private static int? ParseNullableInt(string? value) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed)
                ? parsed
                : null;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record GraphQlRequest(string Query, IReadOnlyDictionary<string, object?> Variables);

    private sealed class GraphQlResponse
    {
        public ProductsData? Data { get; init; }

        public List<GraphQlError>? Errors { get; init; }
    }

    private sealed class GraphQlError
    {
        public string Message { get; init; } = string.Empty;
    }

    private sealed class ProductsData
    {
        public ProductConnection Products { get; init; } = new();
    }

    private sealed class ProductConnection
    {
        public List<ProductEdge> Edges { get; init; } = [];

        public PageInfo PageInfo { get; init; } = new();
    }

    private sealed class ProductEdge
    {
        public string Cursor { get; init; } = string.Empty;

        public ProductNode Node { get; init; } = new();
    }

    private sealed class PageInfo
    {
        public bool HasNextPage { get; init; }
    }

    private sealed class ProductNode
    {
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Handle { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string? DescriptionHtml { get; init; }

        public string? Vendor { get; init; }

        public string? ProductType { get; init; }

        public MetafieldData? UnitLabel { get; init; }

        public MetafieldData? LegacyUnitLabel { get; init; }

        public MetafieldData? ProjectCalculatorType { get; init; }

        public MetafieldData? CoveragePerOrderUnitSqFt { get; init; }

        public MetafieldData? CalculatorOrderUnitLabel { get; init; }

        public MetafieldData? PiecesPerOrderUnit { get; init; }

        public MetafieldData? CalculatorUnitLengthInches { get; init; }

        public MetafieldData? CalculatorUnitHeightInches { get; init; }

        public MetafieldData? LayersPerPallet { get; init; }

        public MetafieldData? SquareFeetPerLayer { get; init; }

        public MetafieldData? PalletWeightLbs { get; init; }

        public List<string> Tags { get; init; } = [];

        public DateTime? CreatedAt { get; init; }

        public DateTime? UpdatedAt { get; init; }

        public DateTime? PublishedAt { get; init; }

        public SeoData? Seo { get; init; }

        public FeaturedMediaData? FeaturedMedia { get; init; }

        public CollectionConnection Collections { get; init; } = new();

        public VariantConnection Variants { get; init; } = new();
    }

    private sealed class SeoData
    {
        public string? Title { get; init; }

        public string? Description { get; init; }
    }

    private sealed class MetafieldData
    {
        public string? Value { get; init; }
    }

    private sealed class FeaturedMediaData
    {
        public ImageData? Image { get; init; }
    }

    private sealed class ImageData
    {
        public string? Url { get; init; }

        public string? AltText { get; init; }
    }

    private sealed class CollectionConnection
    {
        public List<CollectionNode> Nodes { get; init; } = [];
    }

    private sealed class CollectionNode
    {
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string Handle { get; init; } = string.Empty;
    }

    private sealed class VariantConnection
    {
        public List<VariantNode> Nodes { get; init; } = [];
    }

    private sealed class VariantNode
    {
        public string Id { get; init; } = string.Empty;

        public string Title { get; init; } = string.Empty;

        public string? Sku { get; init; }

        public string? Barcode { get; init; }

        public string Price { get; init; } = "0";

        public string? CompareAtPrice { get; init; }

        public ImageData? Image { get; init; }

        public MetafieldData? CoveragePerOrderUnitSqFt { get; init; }

        public MetafieldData? CalculatorOrderUnitLabel { get; init; }

        public MetafieldData? PiecesPerOrderUnit { get; init; }

        public MetafieldData? CalculatorUnitLengthInches { get; init; }

        public MetafieldData? CalculatorUnitHeightInches { get; init; }

        public MetafieldData? LayersPerPallet { get; init; }

        public MetafieldData? SquareFeetPerLayer { get; init; }

        public MetafieldData? PalletWeightLbs { get; init; }

        public bool AvailableForSale { get; init; }
    }

}
