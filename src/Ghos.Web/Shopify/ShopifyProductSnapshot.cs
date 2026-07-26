namespace Ghos.Web.Shopify;

public sealed record ShopifyProductSnapshot(
    string Id,
    string Title,
    string Handle,
    string Status,
    string? DescriptionHtml,
    string? Vendor,
    string? ProductType,
    IReadOnlyList<string> Tags,
    string? SeoTitle,
    string? SeoDescription,
    string? FeaturedImageUrl,
    string? FeaturedImageAlt,
    DateTime? CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? PublishedAtUtc,
    IReadOnlyList<ShopifyCollectionSnapshot> Collections,
    IReadOnlyList<ShopifyVariantSnapshot> Variants);

public sealed record ShopifyVariantSnapshot(
    string Id,
    string Title,
    string? Sku,
    string? Barcode,
    decimal Price,
    decimal? CompareAtPrice,
    bool AvailableForSale);

public sealed record ShopifyCollectionSnapshot(string Id, string Title, string Handle);

public enum ShopifySyncAction
{
    Create,
    Update,
    Unchanged
}

public sealed record ShopifySyncPreviewItem(
    string ShopifyProductId,
    string Title,
    string Handle,
    string ShopifyStatus,
    int VariantCount,
    decimal? LowestPrice,
    string TargetCategory,
    ShopifySyncAction Action);

public sealed record ShopifySyncPreview(
    IReadOnlyList<ShopifySyncPreviewItem> Items)
{
    public int CreateCount => Items.Count(item => item.Action == ShopifySyncAction.Create);

    public int UpdateCount => Items.Count(item => item.Action == ShopifySyncAction.Update);

    public int UnchangedCount => Items.Count(item => item.Action == ShopifySyncAction.Unchanged);
}

public sealed record ShopifySyncResult(
    int Total,
    int Created,
    int Updated,
    int Unchanged,
    DateTime CompletedAtUtc);
