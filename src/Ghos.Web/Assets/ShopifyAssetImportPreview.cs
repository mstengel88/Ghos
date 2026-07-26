namespace Ghos.Web.Assets;

public enum ShopifyAssetImportAction
{
    Download,
    LinkExisting,
    AlreadyLinked,
    InvalidUrl
}

public sealed record ShopifyAssetImportPreviewItem(
    Guid ProductId,
    string ProductName,
    string ImageUrl,
    string? ImageAlt,
    ShopifyAssetImportAction Action);

public sealed record ShopifyAssetImportPreview(
    IReadOnlyList<ShopifyAssetImportPreviewItem> Items)
{
    public int DownloadCount =>
        Items.Count(item => item.Action == ShopifyAssetImportAction.Download);

    public int LinkCount =>
        Items.Count(item => item.Action == ShopifyAssetImportAction.LinkExisting);

    public int CurrentCount =>
        Items.Count(item => item.Action == ShopifyAssetImportAction.AlreadyLinked);

    public int InvalidCount =>
        Items.Count(item => item.Action == ShopifyAssetImportAction.InvalidUrl);
}

public sealed record ShopifyAssetImportResult(
    int Downloaded,
    int Reused,
    int AlreadyCurrent,
    int Failed);
