using Ghos.Web.Data;
using Ghos.Web.Shopify;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.SmartSearch;

public sealed record SmartProductSearchResult(
    Guid ProductId,
    string Title,
    string ProductUrl,
    string? ImageUrl,
    string? Description,
    decimal? StartingPrice,
    int Score,
    IReadOnlyList<string> MatchReasons);

public sealed record SmartProductSearchResponse(
    string Query,
    IReadOnlyList<string> Intents,
    int SynonymMappings,
    IReadOnlyList<SmartProductSearchResult> Results);

public sealed class SmartProductSearchService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<SmartProductSearchResponse> SearchAsync(
        string? query,
        int limit = 12,
        CancellationToken cancellationToken = default)
    {
        var plan = SmartSearchSynonymLibrary.Plan(query);
        if (plan.NormalizedQuery.Length < 2)
        {
            return Empty(plan);
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.ProductCategory)
            .Include(product => product.AlternateNames)
            .Include(product => product.Variants)
            .Include(product => product.ShopifyCollectionLinks)
                .ThenInclude(link => link.ShopifyCollection)
            .Where(product =>
                product.ShopifyHandle != null &&
                product.ShopifyStatus == "ACTIVE")
            .ToListAsync(cancellationToken);

        var results = products
            .Select(product => Score(product, plan))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Title)
            .Take(Math.Clamp(limit, 1, 24))
            .ToList();
        return new SmartProductSearchResponse(
            plan.OriginalQuery,
            plan.Intents,
            SmartSearchSynonymLibrary.SynonymMappingCount,
            results);
    }

    private static SmartProductSearchResult Score(
        Product product,
        SmartSearchQueryPlan plan)
    {
        var fields = new[]
        {
            new SearchField("product name", product.ShopifyTitle ?? product.Name, 100),
            new SearchField(
                "alternate name",
                string.Join(' ', product.AlternateNames.Select(item => item.Name)),
                90),
            new SearchField("best use", product.BestUses, 85),
            new SearchField("category", product.ProductCategory.Name, 55),
            new SearchField("Shopify tag", product.ShopifyTags, 55),
            new SearchField(
                "collection",
                string.Join(
                    ' ',
                    product.ShopifyCollectionLinks.Select(
                        link => link.ShopifyCollection.Title)),
                50),
            new SearchField(
                "size or variant",
                string.Join(' ', product.Variants.Select(variant => variant.Title)),
                50),
            new SearchField(
                "description",
                $"{product.ShortDescription} {product.Description} " +
                $"{ShopifyProductText.FromHtml(product.ShopifyDescriptionHtml)}",
                30)
        };
        var score = 0;
        var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            var normalizedField =
                SmartSearchSynonymLibrary.Normalize(field.Value);
            if (normalizedField.Length == 0)
            {
                continue;
            }

            var directMatches = plan.DirectTerms.Count(term =>
                term.Length > 1 && normalizedField.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase));
            var expandedMatches = plan.ExpandedTerms
                .Except(plan.DirectTerms, StringComparer.OrdinalIgnoreCase)
                .Count(term =>
                    term.Length > 2 && normalizedField.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase));
            if (directMatches == 0 && expandedMatches == 0)
            {
                continue;
            }

            score += directMatches * field.Weight;
            score += Math.Min(expandedMatches, 3) *
                Math.Max(10, field.Weight / 2);
            reasons.Add(
                directMatches > 0
                    ? $"Matches {field.Label}"
                    : $"Related {field.Label}");
        }

        var startingPrice = product.Variants
            .Where(variant => variant.AvailableForSale)
            .Select(variant => (decimal?)variant.Price)
            .Min();
        return new SmartProductSearchResult(
            product.Id,
            product.ShopifyTitle ?? product.Name,
            $"https://greenhillssupply.com/products/{product.ShopifyHandle}",
            product.ShopifyFeaturedImageUrl,
            product.ShortDescription ??
                ShopifyProductText.ToShortDescription(
                    ShopifyProductText.FromHtml(
                        product.ShopifyDescriptionHtml)),
            startingPrice,
            score,
            reasons.Take(3).ToList());
    }

    private static SmartProductSearchResponse Empty(
        SmartSearchQueryPlan plan) =>
        new(
            plan.OriginalQuery,
            plan.Intents,
            SmartSearchSynonymLibrary.SynonymMappingCount,
            []);

    private sealed record SearchField(
        string Label,
        string? Value,
        int Weight);
}
