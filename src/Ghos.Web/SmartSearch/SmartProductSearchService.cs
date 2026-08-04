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
    string Confidence,
    IReadOnlyList<string> MatchedIntents,
    IReadOnlyList<string> UnmatchedIntents,
    IReadOnlyList<string> MatchReasons);

public sealed record SmartProductSearchResponse(
    Guid? SearchEventId,
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
        string source = "Storefront",
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var customSynonyms = await dbContext.SmartSearchSynonymRules
            .AsNoTracking()
            .Where(rule => rule.IsActive)
            .Select(rule => new SmartSearchCustomSynonym(
                rule.Phrase,
                rule.Expansion))
            .ToListAsync(cancellationToken);
        var plan = SmartSearchSynonymLibrary.Plan(
            query,
            customSynonyms);
        if (plan.NormalizedQuery.Length < 2)
        {
            return Empty(plan, customSynonyms.Count);
        }

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
        var searchEvent = new SmartSearchEvent
        {
            Query = Truncate(plan.OriginalQuery, 300),
            NormalizedQuery = Truncate(plan.NormalizedQuery, 300),
            IntentSummary = Truncate(
                string.Join(" · ", plan.Intents),
                500),
            Source = Truncate(source, 32),
            ResultCount = results.Count
        };
        dbContext.SmartSearchEvents.Add(searchEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SmartProductSearchResponse(
            searchEvent.Id,
            plan.OriginalQuery,
            plan.Intents,
            SmartSearchSynonymLibrary.SynonymMappingCount +
                customSynonyms.Count,
            results);
    }

    public async Task<bool> RecordSelectionAsync(
        Guid searchEventId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var searchEvent = await dbContext.SmartSearchEvents
            .SingleOrDefaultAsync(
                item => item.Id == searchEventId,
                cancellationToken);
        if (searchEvent is null ||
            !await dbContext.Products.AnyAsync(
                product =>
                    product.Id == productId &&
                    product.ShopifyStatus == "ACTIVE",
                cancellationToken))
        {
            return false;
        }

        searchEvent.SelectedProductId = productId;
        searchEvent.SelectedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SmartSearchAnalyticsSnapshot> GetAnalyticsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var since = DateTime.UtcNow.AddDays(-7);
        var events = await dbContext.SmartSearchEvents
            .AsNoTracking()
            .Where(item => item.SearchedAtUtc >= since)
            .OrderByDescending(item => item.SearchedAtUtc)
            .Take(2000)
            .ToListAsync(cancellationToken);
        var topQueries = events
            .GroupBy(item => item.NormalizedQuery)
            .Select(group => new SmartSearchQueryStat(
                group.First().Query,
                group.Count(),
                group.Count(item => item.ResultCount == 0)))
            .OrderByDescending(item => item.Searches)
            .ThenBy(item => item.Query)
            .Take(8)
            .ToList();
        var zeroQueries = events
            .Where(item => item.ResultCount == 0)
            .GroupBy(item => item.NormalizedQuery)
            .Select(group => new SmartSearchQueryStat(
                group.First().Query,
                group.Count(),
                group.Count()))
            .OrderByDescending(item => item.Searches)
            .ThenBy(item => item.Query)
            .Take(8)
            .ToList();
        return new SmartSearchAnalyticsSnapshot(
            events.Count,
            events.Count(item => item.ResultCount == 0),
            events.Count(item => item.SelectedProductId is not null),
            topQueries,
            zeroQueries);
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
        var searchableDocument = SmartSearchSynonymLibrary.Normalize(
            string.Join(' ', fields.Select(field => field.Value)));
        var matchedIntents = EvaluateIntentMatches(
            searchableDocument,
            plan);
        var unmatchedIntents = plan.IntentMatches
            .Where(intent => !matchedIntents.Contains(
                $"{intent.Category}: {intent.Name}",
                StringComparer.OrdinalIgnoreCase))
            .Select(intent => $"{intent.Category}: {intent.Name}")
            .ToList();
        foreach (var matchedIntent in matchedIntents)
        {
            var category = matchedIntent.Split(':', 2)[0];
            score += category switch
            {
                "Use" or "Project" => 140,
                "Color" or "Size" => 100,
                _ => 85
            };
            reasons.Add($"Fits {matchedIntent.Split(':', 2)[1].Trim()}");
        }

        foreach (var unmatchedIntent in unmatchedIntents)
        {
            var category = unmatchedIntent.Split(':', 2)[0];
            score -= category is "Use" or "Project" ? 70 : 25;
        }

        foreach (var field in fields)
        {
            var normalizedField =
                SmartSearchSynonymLibrary.Normalize(field.Value);
            if (normalizedField.Length == 0)
            {
                continue;
            }

            var directMatches = plan.DirectTerms.Count(term =>
                term.Length > 1 &&
                SmartSearchSynonymLibrary.ContainsNormalizedTerm(
                    normalizedField,
                    term));
            var expandedMatches = plan.ExpandedTerms
                .Except(plan.DirectTerms, StringComparer.OrdinalIgnoreCase)
                .Count(term =>
                    term.Length > 2 &&
                    SmartSearchSynonymLibrary.ContainsNormalizedTerm(
                        normalizedField,
                        term));
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
            GetConfidence(plan.IntentMatches.Count, matchedIntents.Count, score),
            matchedIntents,
            unmatchedIntents,
            reasons.Take(3).ToList());
    }

    internal static IReadOnlyList<string> EvaluateIntentMatches(
        string searchableDocument,
        SmartSearchQueryPlan plan) =>
        plan.IntentMatches
            .Where(intent => intent.Terms.Any(term =>
                term.Length > 1 &&
                SmartSearchSynonymLibrary.ContainsNormalizedTerm(
                    searchableDocument,
                    term)))
            .Select(intent => $"{intent.Category}: {intent.Name}")
            .ToList();

    private static string GetConfidence(
        int intentCount,
        int matchedIntentCount,
        int score)
    {
        if (intentCount == 0)
        {
            return score >= 100 ? "High" : "Medium";
        }

        if (matchedIntentCount == intentCount)
        {
            return "High";
        }

        return matchedIntentCount * 2 >= intentCount
            ? "Medium"
            : "Low";
    }

    private static SmartProductSearchResponse Empty(
        SmartSearchQueryPlan plan,
        int customSynonymCount = 0) =>
        new(
            null,
            plan.OriginalQuery,
            plan.Intents,
            SmartSearchSynonymLibrary.SynonymMappingCount +
                customSynonymCount,
            []);

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private sealed record SearchField(
        string Label,
        string? Value,
        int Weight);
}

public sealed record SmartSearchQueryStat(
    string Query,
    int Searches,
    int ZeroResults);

public sealed record SmartSearchAnalyticsSnapshot(
    int Searches,
    int ZeroResultSearches,
    int ProductSelections,
    IReadOnlyList<SmartSearchQueryStat> TopQueries,
    IReadOnlyList<SmartSearchQueryStat> ZeroResultQueries);
