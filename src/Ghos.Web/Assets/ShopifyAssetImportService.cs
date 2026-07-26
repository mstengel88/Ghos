using System.Net.Http.Headers;
using System.Security.Claims;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Assets;

public sealed class ShopifyAssetImportService(
    HttpClient httpClient,
    AssetStorageService storageService,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<ShopifyAssetImportService> logger)
{
    private const string ShopifyCdnHost = "cdn.shopify.com";

    public async Task<ShopifyAssetImportPreview> PreviewAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => product.ShopifyFeaturedImageUrl != null)
            .Select(product => new
            {
                product.Id,
                product.Name,
                ImageUrl = product.ShopifyFeaturedImageUrl!,
                ImageAlt = product.ShopifyFeaturedImageAlt
            })
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        var sourceUrls = products
            .Select(product => product.ImageUrl)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var existingAssets = await dbContext.DigitalAssets
            .AsNoTracking()
            .Where(asset =>
                asset.SourceUrl != null &&
                sourceUrls.Contains(asset.SourceUrl))
            .Select(asset => new
            {
                asset.Id,
                asset.SourceUrl
            })
            .ToListAsync(cancellationToken);
        var assetByUrl = existingAssets
            .Where(asset => asset.SourceUrl is not null)
            .ToDictionary(asset => asset.SourceUrl!, StringComparer.Ordinal);
        var assetIds = existingAssets.Select(asset => asset.Id).ToList();
        var existingLinks = await dbContext.AssetProductLinks
            .AsNoTracking()
            .Where(link => assetIds.Contains(link.DigitalAssetId))
            .Select(link => new
            {
                link.DigitalAssetId,
                link.ProductId
            })
            .ToListAsync(cancellationToken);
        var linkedPairs = existingLinks
            .Select(link => (link.DigitalAssetId, link.ProductId))
            .ToHashSet();

        var items = products
            .Select(product =>
            {
                var action = !TryValidateShopifyImageUrl(product.ImageUrl, out _)
                    ? ShopifyAssetImportAction.InvalidUrl
                    : !assetByUrl.TryGetValue(product.ImageUrl, out var asset)
                        ? ShopifyAssetImportAction.Download
                        : linkedPairs.Contains((asset.Id, product.Id))
                            ? ShopifyAssetImportAction.AlreadyLinked
                            : ShopifyAssetImportAction.LinkExisting;

                return new ShopifyAssetImportPreviewItem(
                    product.Id,
                    product.Name,
                    product.ImageUrl,
                    product.ImageAlt,
                    action);
            })
            .ToList();

        return new ShopifyAssetImportPreview(items);
    }

    public async Task<ShopifyAssetImportResult> ImportMissingAsync(
        ClaimsPrincipal user,
        int? maximumItems = null,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(cancellationToken);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var downloaded = 0;
        var reused = 0;
        var alreadyCurrent = 0;
        var failed = 0;

        var items = maximumItems is > 0
            ? preview.Items.Take(maximumItems.Value)
            : preview.Items;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Action == ShopifyAssetImportAction.AlreadyLinked)
            {
                alreadyCurrent++;
                continue;
            }

            if (item.Action == ShopifyAssetImportAction.InvalidUrl)
            {
                failed++;
                continue;
            }

            try
            {
                if (item.Action == ShopifyAssetImportAction.LinkExisting)
                {
                    await LinkExistingAsync(
                        item.ProductId,
                        item.ImageUrl,
                        userId,
                        cancellationToken);
                    reused++;
                    continue;
                }

                var created = await ImportImageAsync(
                    item,
                    userId,
                    cancellationToken);

                if (created)
                {
                    downloaded++;
                }
                else
                {
                    reused++;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or
                AssetStorageException or
                InvalidOperationException)
            {
                failed++;
                logger.LogWarning(
                    exception,
                    "Could not import the Shopify image for product {ProductId}.",
                    item.ProductId);
            }
        }

        return new ShopifyAssetImportResult(
            downloaded,
            reused,
            alreadyCurrent,
            failed);
    }

    private async Task<bool> ImportImageAsync(
        ShopifyAssetImportPreviewItem item,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (!TryValidateShopifyImageUrl(item.ImageUrl, out var sourceUri))
        {
            throw new AssetStorageException("Shopify supplied an invalid image URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Shopify CDN returned HTTP {(int)response.StatusCode}.");
        }

        if (response.Headers.Location is not null)
        {
            throw new HttpRequestException("Shopify CDN returned an unexpected redirect.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;

        if (contentType is null ||
            !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new AssetStorageException("Shopify returned a file that is not an image.");
        }

        var contentLength = response.Content.Headers.ContentLength;
        var fileSizeBytes = contentLength.GetValueOrDefault();

        if (fileSizeBytes <= 0)
        {
            throw new AssetStorageException("Shopify returned an empty image.");
        }

        if (fileSizeBytes > storageService.MaxFileSizeBytes)
        {
            throw new AssetStorageException(
                $"The Shopify image is larger than the {AssetStorageService.FormatBytes(storageService.MaxFileSizeBytes)} asset limit.");
        }

        var fileName = GetFileName(sourceUri, contentType);
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await storageService.StoreAsync(
            new AssetUploadRequest(
                content,
                fileName,
                fileSizeBytes,
                item.ImageAlt ?? $"{item.ProductName} product image",
                $"Featured product image imported from Shopify for {item.ProductName}.",
                $"Shopify, product image, {item.ProductName}",
                0,
                item.ProductId,
                userId,
                AssetSource.Shopify,
                sourceUri.AbsoluteUri),
            cancellationToken);

        return result.Created;
    }

    private async Task LinkExistingAsync(
        Guid productId,
        string sourceUrl,
        string? userId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var asset = await dbContext.DigitalAssets
            .Include(item => item.ProductLinks)
            .SingleAsync(item => item.SourceUrl == sourceUrl, cancellationToken);

        if (asset.ProductLinks.All(link => link.ProductId != productId))
        {
            asset.ProductLinks.Add(new AssetProductLink { ProductId = productId });
            asset.UpdatedAtUtc = DateTime.UtcNow;
            asset.UpdatedByUserId = userId;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool TryValidateShopifyImageUrl(
        string sourceUrl,
        out Uri sourceUri)
    {
        if (Uri.TryCreate(sourceUrl, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.Equals(
                parsed.Host,
                ShopifyCdnHost,
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(parsed.UserInfo))
        {
            sourceUri = parsed;
            return true;
        }

        sourceUri = null!;
        return false;
    }

    private static string GetFileName(Uri sourceUri, string contentType)
    {
        var fileName = Path.GetFileName(Uri.UnescapeDataString(sourceUri.AbsolutePath));

        if (!string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
        {
            return fileName;
        }

        var extension = contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/heic" => ".heic",
            "image/heif" => ".heif",
            _ => throw new AssetStorageException(
                "Shopify returned an unsupported image format.")
        };

        return $"shopify-image{extension}";
    }
}
