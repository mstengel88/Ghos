using System.Security.Claims;
using Ghos.Web.Data;
using Ghos.Web.Products;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Shopify;

public sealed class ShopifySyncService(
    ShopifyAdminClient shopifyClient,
    ShopifyCredentialStore credentialStore,
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    ILogger<ShopifySyncService> logger)
{
    private static readonly HashSet<string> GenericCollectionHandles =
    [
        "all",
        "all-products",
        "frontpage",
        "summer-scape",
        "summerscape-collection"
    ];

    private static readonly Dictionary<string, string> CategoryAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["soil"] = "topsoil-soil",
            ["bagged-landscape-materials"] = "bagged-materials",
            ["bulk-salt"] = "ice-melt",
            ["bagged-ice-melt"] = "ice-melt",
            ["de-icing-liquid"] = "ice-melt",
            ["tools-hardware"] = "tools-equipment",
            ["boulders-outcropping"] = "boulders-outcroppings",
            ["landscape-accessories"] = "landscape-essentials"
        };

    private static readonly HashSet<string> PreferredCategoryCollectionHandles =
    [
        "boulders-outcroppings",
        "boulders-outcropping",
        "landscape-essentials",
        "landscape-accessories"
    ];

    public string StoreDomain => shopifyClient.StoreDomain;

    public string StorefrontUrl => shopifyClient.StorefrontUrl;

    public string ApiVersion => shopifyClient.ApiVersion;

    public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
        credentialStore.HasCredentialsAsync(cancellationToken);

    public async Task<ShopifySyncPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = await shopifyClient.GetProductsAsync(cancellationToken);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existingProducts = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.Variants)
            .Include(product => product.ShopifyCollectionLinks)
                .ThenInclude(link => link.ShopifyCollection)
            .Where(product => product.ShopifyProductId != null)
            .ToListAsync(cancellationToken);
        var categories = await dbContext.ProductCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
        var fallbackCategory = GetFallbackCategory(categories);
        var byShopifyId = existingProducts.ToDictionary(
            product => product.ShopifyProductId!,
            StringComparer.Ordinal);

        var items = snapshots
            .Select(snapshot =>
            {
                var resolvedCategory = ResolveCategory(snapshot, categories);
                var action = !byShopifyId.TryGetValue(snapshot.Id, out var existing)
                    ? ShopifySyncAction.Create
                    : SourceHasChanged(existing, snapshot) ||
                        CollectionMembershipsChanged(existing, snapshot) ||
                        ShouldRecoverCategory(existing, resolvedCategory, fallbackCategory)
                        ? ShopifySyncAction.Update
                        : ShopifySyncAction.Unchanged;

                return new ShopifySyncPreviewItem(
                    snapshot.Id,
                    snapshot.Title,
                    snapshot.Handle,
                    snapshot.Status,
                    snapshot.Variants.Count,
                    snapshot.Variants.Count == 0
                        ? null
                        : snapshot.Variants.Min(variant => variant.Price),
                    resolvedCategory.Name,
                    action);
            })
            .OrderBy(item => item.Title)
            .ToList();

        return new ShopifySyncPreview(items);
    }

    public async Task<ShopifySyncResult> SynchronizeAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await shopifyClient.GetProductsAsync(cancellationToken);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var syncRun = new ShopifySyncRun
        {
            InitiatedByUserId = userId,
            ShopifyProductCount = snapshots.Count
        };

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.ShopifySyncRuns.Add(syncRun);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var products = await dbContext.Products
                .Include(product => product.Variants)
                .Include(product => product.ShopifyCollectionLinks)
                    .ThenInclude(link => link.ShopifyCollection)
                .ToListAsync(cancellationToken);
            var shopifyCollections = await dbContext.ShopifyCollections
                .ToListAsync(cancellationToken);
            var collectionsByShopifyId = shopifyCollections.ToDictionary(
                collection => collection.ShopifyCollectionId,
                StringComparer.Ordinal);
            var categories = await dbContext.ProductCategories
                .Where(category => category.IsActive)
                .OrderBy(category => category.SortOrder)
                .ThenBy(category => category.Name)
                .ToListAsync(cancellationToken);
            var fallbackCategory = GetFallbackCategory(categories);
            var byShopifyId = products
                .Where(product => product.ShopifyProductId is not null)
                .ToDictionary(product => product.ShopifyProductId!, StringComparer.Ordinal);
            var byHandle = products
                .Where(product => product.ShopifyHandle is not null)
                .GroupBy(product => product.ShopifyHandle!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var usedSlugs = products
                .Select(product => product.Slug)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var usedProductCodes = products
                .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                .Select(product => product.ProductCode!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var created = 0;
            var updated = 0;
            var unchanged = 0;

            foreach (var snapshot in snapshots)
            {
                var resolvedCategory = ResolveCategory(snapshot, categories);
                var isNew = !byShopifyId.TryGetValue(snapshot.Id, out var product);

                if (isNew &&
                    byHandle.TryGetValue(snapshot.Handle, out var handleMatch) &&
                    handleMatch.ShopifyProductId is null)
                {
                    product = handleMatch;
                    isNew = false;
                }

                if (product is null)
                {
                    product = CreateProduct(
                        snapshot,
                        resolvedCategory,
                        usedSlugs,
                        usedProductCodes,
                        userId,
                        now);
                    dbContext.Products.Add(product);
                    products.Add(product);
                    created++;
                }
                else if (SourceHasChanged(product, snapshot) ||
                    CollectionMembershipsChanged(product, snapshot) ||
                    product.ShopifyProductId is null ||
                    ShouldRecoverCategory(product, resolvedCategory, fallbackCategory))
                {
                    UpdateSourceFields(product, snapshot, now);
                    if (ShouldRecoverCategory(product, resolvedCategory, fallbackCategory))
                    {
                        product.ProductCategoryId = resolvedCategory.Id;
                        product.ProductCategory = resolvedCategory;
                    }
                    product.UpdatedAtUtc = now;
                    product.UpdatedByUserId = userId;
                    MergeVariants(product, snapshot);
                    updated++;
                }
                else
                {
                    unchanged++;
                }

                product.ShopifyProductId = snapshot.Id;
                MergeCollections(
                    product,
                    snapshot,
                    collectionsByShopifyId,
                    dbContext,
                    now);
                byShopifyId[snapshot.Id] = product;
                byHandle[snapshot.Handle] = product;
            }

            syncRun.Status = "Succeeded";
            syncRun.CreatedCount = created;
            syncRun.UpdatedCount = updated;
            syncRun.UnchangedCount = unchanged;
            syncRun.CompletedAtUtc = now;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new ShopifySyncResult(
                snapshots.Count,
                created,
                updated,
                unchanged,
                now);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Shopify product synchronization failed.");
            dbContext.ChangeTracker.Clear();
            var persistedRun = await dbContext.ShopifySyncRuns
                .SingleAsync(run => run.Id == syncRun.Id, cancellationToken);
            persistedRun.Status = "Failed";
            persistedRun.CompletedAtUtc = DateTime.UtcNow;
            persistedRun.ErrorMessage = exception.Message.Length <= 2000
                ? exception.Message
                : exception.Message[..2000];
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ShopifySyncRun?> GetLastRunAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.ShopifySyncRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static Product CreateProduct(
        ShopifyProductSnapshot snapshot,
        ProductCategory category,
        ISet<string> usedSlugs,
        ISet<string> usedProductCodes,
        string? userId,
        DateTime now)
    {
        var description = ShopifyProductText.FromHtml(snapshot.DescriptionHtml);
        var firstSku = snapshot.Variants
            .Select(variant => variant.Sku)
            .FirstOrDefault(sku => !string.IsNullOrWhiteSpace(sku));
        var productCode = !string.IsNullOrWhiteSpace(firstSku) &&
            usedProductCodes.Add(firstSku)
                ? firstSku
                : null;
        var product = new Product
        {
            Name = snapshot.Title.Trim(),
            Slug = CreateUniqueSlug(snapshot.Handle, usedSlugs),
            ProductCode = productCode,
            ProductCategoryId = category.Id,
            ProductCategory = category,
            Status = MapStatus(snapshot.Status),
            ShortDescription = ShopifyProductText.ToShortDescription(description),
            Description = description,
            AvailableForPickup = false,
            AvailableForDelivery = false,
            AvailableInBulk = false,
            AvailableBagged = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedByUserId = userId,
            UpdatedByUserId = userId
        };

        UpdateSourceFields(product, snapshot, now);
        MergeVariants(product, snapshot);
        return product;
    }

    private static void UpdateSourceFields(
        Product product,
        ShopifyProductSnapshot snapshot,
        DateTime syncedAtUtc)
    {
        product.ShopifyProductId = snapshot.Id;
        product.ShopifyTitle = snapshot.Title.Trim();
        product.ShopifyHandle = snapshot.Handle.Trim();
        product.ShopifyStatus = snapshot.Status;
        product.ShopifyVendor = snapshot.Vendor;
        product.ShopifyProductType = snapshot.ProductType;
        product.ShopifyDescriptionHtml = snapshot.DescriptionHtml;
        product.ShopifyTags = snapshot.Tags.Count == 0
            ? null
            : string.Join(Environment.NewLine, snapshot.Tags);
        product.ShopifyFeaturedImageUrl = snapshot.FeaturedImageUrl;
        product.ShopifyFeaturedImageAlt = snapshot.FeaturedImageAlt;
        product.ShopifySeoTitle = snapshot.SeoTitle;
        product.ShopifySeoDescription = snapshot.SeoDescription;
        product.ShopifyCreatedAtUtc = ToUtc(snapshot.CreatedAtUtc);
        product.ShopifyUpdatedAtUtc = ToUtc(snapshot.UpdatedAtUtc);
        product.ShopifyPublishedAtUtc = ToUtc(snapshot.PublishedAtUtc);
        product.ShopifyLastSyncedAtUtc = syncedAtUtc;
    }

    private static void MergeVariants(Product product, ShopifyProductSnapshot snapshot)
    {
        var incomingIds = snapshot.Variants
            .Select(variant => variant.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedVariants = product.Variants
            .Where(variant => !incomingIds.Contains(variant.ShopifyVariantId))
            .ToList();

        foreach (var removedVariant in removedVariants)
        {
            product.Variants.Remove(removedVariant);
        }

        var existingByShopifyId = product.Variants.ToDictionary(
            variant => variant.ShopifyVariantId,
            StringComparer.Ordinal);

        for (var index = 0; index < snapshot.Variants.Count; index++)
        {
            var incoming = snapshot.Variants[index];

            if (!existingByShopifyId.TryGetValue(incoming.Id, out var variant))
            {
                variant = new ProductVariant
                {
                    ShopifyVariantId = incoming.Id
                };
                product.Variants.Add(variant);
            }

            variant.Title = incoming.Title;
            variant.Sku = incoming.Sku;
            variant.Barcode = incoming.Barcode;
            variant.Price = incoming.Price;
            variant.CompareAtPrice = incoming.CompareAtPrice;
            variant.AvailableForSale = incoming.AvailableForSale;
            variant.SortOrder = index;
        }
    }

    private static void MergeCollections(
        Product product,
        ShopifyProductSnapshot snapshot,
        IDictionary<string, ShopifyCollection> collectionsByShopifyId,
        ApplicationDbContext dbContext,
        DateTime syncedAtUtc)
    {
        var incomingIds = snapshot.Collections
            .Select(collection => collection.Id)
            .ToHashSet(StringComparer.Ordinal);
        var removedLinks = product.ShopifyCollectionLinks
            .Where(link => !incomingIds.Contains(link.ShopifyCollection.ShopifyCollectionId))
            .ToList();

        foreach (var removedLink in removedLinks)
        {
            product.ShopifyCollectionLinks.Remove(removedLink);
        }

        var linkedCollectionIds = product.ShopifyCollectionLinks
            .Select(link => link.ShopifyCollection.ShopifyCollectionId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var snapshotCollection in snapshot.Collections)
        {
            if (!collectionsByShopifyId.TryGetValue(snapshotCollection.Id, out var collection))
            {
                collection = new ShopifyCollection
                {
                    ShopifyCollectionId = snapshotCollection.Id
                };
                collectionsByShopifyId[snapshotCollection.Id] = collection;
                dbContext.ShopifyCollections.Add(collection);
            }

            collection.Title = snapshotCollection.Title.Trim();
            collection.Handle = snapshotCollection.Handle.Trim();
            collection.LastSyncedAtUtc = syncedAtUtc;

            if (linkedCollectionIds.Add(snapshotCollection.Id))
            {
                product.ShopifyCollectionLinks.Add(new ProductShopifyCollection
                {
                    Product = product,
                    ShopifyCollection = collection
                });
            }
        }
    }

    private static bool SourceHasChanged(Product product, ShopifyProductSnapshot snapshot)
    {
        if (product.ShopifyTitle != snapshot.Title ||
            product.ShopifyHandle != snapshot.Handle ||
            product.ShopifyStatus != snapshot.Status ||
            product.ShopifyUpdatedAtUtc != ToUtc(snapshot.UpdatedAtUtc) ||
            product.Variants.Count != snapshot.Variants.Count)
        {
            return true;
        }

        var existingVariants = product.Variants
            .OrderBy(variant => variant.SortOrder)
            .ToList();

        for (var index = 0; index < snapshot.Variants.Count; index++)
        {
            var existing = existingVariants[index];
            var incoming = snapshot.Variants[index];

            if (existing.ShopifyVariantId != incoming.Id ||
                existing.Title != incoming.Title ||
                existing.Sku != incoming.Sku ||
                existing.Barcode != incoming.Barcode ||
                existing.Price != incoming.Price ||
                existing.CompareAtPrice != incoming.CompareAtPrice ||
                existing.AvailableForSale != incoming.AvailableForSale)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CollectionMembershipsChanged(
        Product product,
        ShopifyProductSnapshot snapshot)
    {
        var existing = product.ShopifyCollectionLinks
            .Select(link => link.ShopifyCollection.ShopifyCollectionId)
            .ToHashSet(StringComparer.Ordinal);
        var incoming = snapshot.Collections
            .Select(collection => collection.Id)
            .ToHashSet(StringComparer.Ordinal);

        return !existing.SetEquals(incoming);
    }

    private static ProductCategory ResolveCategory(
        ShopifyProductSnapshot snapshot,
        IList<ProductCategory> categories)
    {
        foreach (var collection in snapshot.Collections
            .OrderBy(collection =>
                PreferredCategoryCollectionHandles.Contains(collection.Handle) ? 0 : 1))
        {
            if (GenericCollectionHandles.Contains(collection.Handle))
            {
                continue;
            }

            var collectionKey = ProductSlug.Create(collection.Handle);
            var categoryKey = CategoryAliases.GetValueOrDefault(collectionKey, collectionKey);
            var category = categories.FirstOrDefault(candidate =>
                candidate.Slug.Equals(categoryKey, StringComparison.OrdinalIgnoreCase) ||
                ProductSlug.Create(candidate.Name).Equals(categoryKey, StringComparison.OrdinalIgnoreCase));

            if (category is not null)
            {
                return category;
            }
        }

        return GetFallbackCategory(categories);
    }

    private static ProductCategory GetFallbackCategory(
        IEnumerable<ProductCategory> categories) =>
        categories.Single(category =>
            category.Slug.Equals("shopify-import", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldRecoverCategory(
        Product product,
        ProductCategory resolvedCategory,
        ProductCategory fallbackCategory) =>
        product.ProductCategoryId == fallbackCategory.Id &&
        resolvedCategory.Id != fallbackCategory.Id;

    private static string CreateUniqueSlug(string preferredSlug, ISet<string> usedSlugs)
    {
        var baseSlug = ProductSlug.Create(preferredSlug);
        var candidate = baseSlug;
        var suffix = 2;

        while (!usedSlugs.Add(candidate))
        {
            candidate = $"{baseSlug}-{suffix++}";
        }

        return candidate;
    }

    private static ProductStatus MapStatus(string status) =>
        status.ToUpperInvariant() switch
        {
            "ACTIVE" => ProductStatus.Active,
            "ARCHIVED" => ProductStatus.Archived,
            _ => ProductStatus.Draft
        };

    private static DateTime? ToUtc(DateTime? value) =>
        value?.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ when value is not null => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => null
        };
}
