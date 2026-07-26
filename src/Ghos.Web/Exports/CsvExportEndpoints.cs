using System.Globalization;
using System.Text;
using Ghos.Web.Auth;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Exports;

public static class CsvExportEndpoints
{
    public static IEndpointRouteBuilder MapCsvExportEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/exports");

        group.MapGet("/products.csv", ExportProductsAsync)
            .RequireAuthorization(GhosPolicies.Operations);

        group.MapGet("/assets.csv", ExportAssetsAsync)
            .RequireAuthorization(GhosPolicies.Assets);

        return endpoints;
    }

    private static async Task<IResult> ExportProductsAsync(
        string? search,
        Guid? categoryId,
        ProductStatus? status,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.Products
            .AsNoTracking()
            .Include(product => product.ProductCategory)
            .Include(product => product.AlternateNames)
            .Include(product => product.Variants)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(product =>
                EF.Functions.ILike(product.Name, $"%{term}%") ||
                (product.ProductCode != null &&
                    EF.Functions.ILike(product.ProductCode, $"%{term}%")) ||
                (product.ShopifyHandle != null &&
                    EF.Functions.ILike(product.ShopifyHandle, $"%{term}%")));
        }

        if (categoryId.HasValue)
        {
            query = query.Where(product =>
                product.ProductCategoryId == categoryId.Value);
        }

        if (status.HasValue && Enum.IsDefined(status.Value))
        {
            query = query.Where(product => product.Status == status.Value);
        }

        var products = await query
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

        var csv = new CsvDocument();
        csv.AddRow(
            "Name",
            "Product code",
            "Category",
            "Status",
            "Pickup",
            "Delivery",
            "Bulk",
            "Bagged",
            "Alternate names",
            "Variants",
            "Shopify product ID",
            "Shopify handle",
            "Shopify status",
            "Last Shopify sync (UTC)",
            "Last updated (UTC)");

        foreach (var product in products)
        {
            csv.AddRow(
                product.Name,
                product.ProductCode,
                product.ProductCategory.Name,
                product.Status.ToString(),
                YesNo(product.AvailableForPickup),
                YesNo(product.AvailableForDelivery),
                YesNo(product.AvailableInBulk),
                YesNo(product.AvailableBagged),
                string.Join("; ", product.AlternateNames
                    .OrderBy(item => item.Name)
                    .Select(item => item.Name)),
                string.Join("; ", product.Variants
                    .OrderBy(item => item.SortOrder)
                    .Select(item => string.IsNullOrWhiteSpace(item.Sku)
                        ? item.Title
                        : $"{item.Title} [{item.Sku}]")),
                product.ShopifyProductId,
                product.ShopifyHandle,
                product.ShopifyStatus,
                FormatDate(product.ShopifyLastSyncedAtUtc),
                FormatDate(product.UpdatedAtUtc));
        }

        return CsvFile(csv, $"ghos-products-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private static async Task<IResult> ExportAssetsAsync(
        string? search,
        AssetStatus? status,
        AssetSource? source,
        string? link,
        IDbContextFactory<ApplicationDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.DigitalAssets
            .AsNoTracking()
            .Include(asset => asset.ProductLinks)
                .ThenInclude(productLink => productLink.Product)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(asset =>
                EF.Functions.ILike(asset.Title, $"%{term}%") ||
                EF.Functions.ILike(asset.OriginalFileName, $"%{term}%") ||
                (asset.Tags != null && EF.Functions.ILike(asset.Tags, $"%{term}%")) ||
                asset.ProductLinks.Any(productLink =>
                    EF.Functions.ILike(productLink.Product.Name, $"%{term}%")));
        }

        if (status.HasValue && Enum.IsDefined(status.Value))
        {
            query = query.Where(asset => asset.Status == status.Value);
        }

        if (source.HasValue && Enum.IsDefined(source.Value))
        {
            query = query.Where(asset => asset.Source == source.Value);
        }

        if (string.Equals(link, "linked", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(asset => asset.ProductLinks.Any());
        }
        else if (string.Equals(link, "unlinked", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(asset => !asset.ProductLinks.Any());
        }

        var assets = await query
            .OrderByDescending(asset => asset.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var csv = new CsvDocument();
        csv.AddRow(
            "Title",
            "Original filename",
            "Kind",
            "Status",
            "Source",
            "Rating",
            "Tags",
            "Linked products",
            "Primary for products",
            "Content type",
            "File size (bytes)",
            "Source URL",
            "Captured (UTC)",
            "Approved (UTC)",
            "Created (UTC)",
            "Last updated (UTC)");

        foreach (var asset in assets)
        {
            csv.AddRow(
                asset.Title,
                asset.OriginalFileName,
                asset.Kind.ToString(),
                FormatAssetStatus(asset.Status),
                FormatAssetSource(asset.Source),
                asset.Rating.ToString(CultureInfo.InvariantCulture),
                asset.Tags,
                string.Join("; ", asset.ProductLinks
                    .OrderBy(item => item.Product.Name)
                    .Select(item => item.Product.Name)),
                string.Join("; ", asset.ProductLinks
                    .Where(item => item.IsPrimary)
                    .OrderBy(item => item.Product.Name)
                    .Select(item => item.Product.Name)),
                asset.ContentType,
                asset.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
                asset.SourceUrl,
                FormatDate(asset.CapturedAtUtc),
                FormatDate(asset.ApprovedAtUtc),
                FormatDate(asset.CreatedAtUtc),
                FormatDate(asset.UpdatedAtUtc));
        }

        return CsvFile(csv, $"ghos-assets-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    private static IResult CsvFile(CsvDocument document, string fileName)
    {
        var content = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(document.ToString()))
            .ToArray();

        return Results.File(content, "text/csv; charset=utf-8", fileName);
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string? FormatDate(DateTime? value) =>
        value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatAssetStatus(AssetStatus status) =>
        status == AssetStatus.PendingReview ? "Pending review" : status.ToString();

    private static string FormatAssetSource(AssetSource source) =>
        source == AssetSource.ICloudImport ? "iCloud import" : source.ToString();

    private sealed class CsvDocument
    {
        private readonly StringBuilder builder = new();

        public void AddRow(params string?[] values)
        {
            builder.AppendLine(string.Join(",", values.Select(EscapeCell)));
        }

        public override string ToString() => builder.ToString();

        private static string EscapeCell(string? value)
        {
            var safeValue = value ?? string.Empty;
            if (safeValue.Length > 0 &&
                safeValue[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            {
                safeValue = $"'{safeValue}";
            }

            return $"\"{safeValue.Replace("\"", "\"\"")}\"";
        }
    }
}
