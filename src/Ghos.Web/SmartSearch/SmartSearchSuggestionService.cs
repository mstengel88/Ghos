using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ghos.Web.SmartSearch;

public sealed record SmartSearchSuggestion(
    string Text,
    string Kind,
    string? Subtitle,
    string? ProductUrl,
    string? ImageUrl);

public sealed record SmartSearchSuggestionCandidate(
    string Text,
    string Kind,
    string? Subtitle = null,
    string? ProductUrl = null,
    string? ImageUrl = null,
    int Popularity = 0);

public sealed class SmartSearchSuggestionService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IMemoryCache memoryCache)
{
    private const string CandidateCacheKey =
        "smart-search-suggestion-candidates-v1";

    public async Task<IReadOnlyList<SmartSearchSuggestion>>
        GetSuggestionsAsync(
            string? query,
            int limit = 8,
            CancellationToken cancellationToken = default)
    {
        var normalized = SmartSearchSynonymLibrary.Normalize(query);
        if (normalized.Length < 2)
        {
            return [];
        }

        normalized = normalized.Length <= 120
            ? normalized
            : normalized[..120];
        var candidates = await memoryCache.GetOrCreateAsync(
            CandidateCacheKey,
            async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromMinutes(5);
                return await LoadCandidatesAsync(cancellationToken);
            }) ?? [];
        return SmartSearchSuggestionRanker.Rank(
            normalized,
            candidates,
            Math.Clamp(limit, 1, 10));
    }

    private async Task<List<SmartSearchSuggestionCandidate>>
        LoadCandidatesAsync(CancellationToken cancellationToken)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var products = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.ProductCategory)
            .Include(product => product.ShopifyCollectionLinks)
                .ThenInclude(link => link.ShopifyCollection)
            .Where(product =>
                product.ShopifyHandle != null &&
                product.ShopifyStatus == "ACTIVE")
            .ToListAsync(cancellationToken);
        var customSynonyms = await dbContext.SmartSearchSynonymRules
            .AsNoTracking()
            .Where(rule => rule.IsActive)
            .Select(rule => new
            {
                rule.Phrase,
                rule.Expansion
            })
            .ToListAsync(cancellationToken);
        var since = DateTime.UtcNow.AddDays(-30);
        var successfulSearches = await dbContext.SmartSearchEvents
            .AsNoTracking()
            .Where(item =>
                item.SearchedAtUtc >= since &&
                item.ResultCount > 0)
            .OrderByDescending(item => item.SearchedAtUtc)
            .Take(2000)
            .Select(item => item.Query)
            .ToListAsync(cancellationToken);
        var candidates = new List<SmartSearchSuggestionCandidate>();

        candidates.AddRange(products.Select(product =>
            new SmartSearchSuggestionCandidate(
                product.ShopifyTitle ?? product.Name,
                "Product",
                product.ProductCategory.Name,
                $"https://greenhillssupply.com/products/" +
                    $"{product.ShopifyHandle}",
                product.ShopifyFeaturedImageUrl)));
        candidates.AddRange(products
            .Select(product => product.ProductCategory.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(category =>
                new SmartSearchSuggestionCandidate(
                    category,
                    "Category",
                    "Product category")));
        candidates.AddRange(products
            .SelectMany(product =>
                product.ShopifyCollectionLinks.Select(link =>
                    link.ShopifyCollection.Title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(collection =>
                new SmartSearchSuggestionCandidate(
                    collection,
                    "Collection",
                    "Shopify collection")));
        candidates.AddRange(
            SmartSearchSynonymLibrary.VocabularyPhrases
                .Where(phrase => phrase.Length is >= 3 and <= 80)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(phrase =>
                    new SmartSearchSuggestionCandidate(
                        phrase,
                        "Search",
                        "Suggested search")));
        candidates.AddRange(customSynonyms.SelectMany(rule =>
            new[]
            {
                new SmartSearchSuggestionCandidate(
                    rule.Phrase,
                    "Search",
                    "Approved customer term"),
                new SmartSearchSuggestionCandidate(
                    rule.Expansion,
                    "Search",
                    "Catalog term")
            }));
        candidates.AddRange(successfulSearches
            .GroupBy(
                query => SmartSearchSynonymLibrary.Normalize(query),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length >= 2)
            .Select(group =>
                new SmartSearchSuggestionCandidate(
                    group.First(),
                    "Popular",
                    "Successful customer search",
                    Popularity: group.Count())));
        return candidates;
    }
}

public static class SmartSearchSuggestionRanker
{
    public static IReadOnlyList<SmartSearchSuggestion> Rank(
        string? query,
        IEnumerable<SmartSearchSuggestionCandidate> candidates,
        int limit = 8)
    {
        var normalizedQuery =
            SmartSearchSynonymLibrary.Normalize(query);
        if (normalizedQuery.Length < 2)
        {
            return [];
        }

        return candidates
            .Select(candidate => new
            {
                Candidate = candidate,
                Score = Score(normalizedQuery, candidate)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Text)
            .GroupBy(
                item => SmartSearchSynonymLibrary.Normalize(
                    item.Candidate.Text),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Clamp(limit, 1, 10))
            .Select(item => new SmartSearchSuggestion(
                item.Candidate.Text,
                item.Candidate.Kind,
                item.Candidate.Subtitle,
                item.Candidate.ProductUrl,
                item.Candidate.ImageUrl))
            .ToList();
    }

    private static int Score(
        string normalizedQuery,
        SmartSearchSuggestionCandidate candidate)
    {
        var normalizedText =
            SmartSearchSynonymLibrary.Normalize(candidate.Text);
        if (normalizedText.Length == 0)
        {
            return 0;
        }

        var matchScore =
            normalizedText.StartsWith(
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase)
                ? 120
                : normalizedText
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => word.StartsWith(
                        normalizedQuery,
                        StringComparison.OrdinalIgnoreCase))
                    ? 95
                    : normalizedText.Contains(
                        normalizedQuery,
                        StringComparison.OrdinalIgnoreCase)
                        ? 70
                        : 0;
        if (matchScore == 0)
        {
            return 0;
        }

        return matchScore +
            (candidate.Kind == "Product" ? 35 : 0) +
            (candidate.Kind == "Popular" ? 10 : 0) +
            Math.Min(candidate.Popularity, 20);
    }
}
