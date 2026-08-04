using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.SmartSearch;

public sealed record SmartSearchProductOption(
    Guid ProductId,
    string Title);

public sealed class SmartSearchMerchandisingService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<SmartSearchMerchandisingRule>>
        GetRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SmartSearchMerchandisingRules
            .AsNoTracking()
            .Include(rule => rule.Product)
            .OrderByDescending(rule => rule.IsActive)
            .ThenBy(rule => rule.QueryPhrase)
            .ThenBy(rule => rule.Product.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SmartSearchProductOption>>
        GetProductOptionsAsync(
            CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ShopifyHandle != null &&
                product.ShopifyStatus == "ACTIVE")
            .OrderBy(product =>
                product.ShopifyTitle ?? product.Name)
            .Select(product => new SmartSearchProductOption(
                product.Id,
                product.ShopifyTitle ?? product.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<SmartSearchMerchandisingRule> AddRuleAsync(
        string? queryPhrase,
        Guid productId,
        string? ruleType,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery =
            SmartSearchTuningService.NormalizeRequired(
                queryPhrase,
                "Customer search");
        var normalizedRuleType = NormalizeRuleType(ruleType);

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await dbContext.Products.AnyAsync(
            product =>
                product.Id == productId &&
                product.ShopifyHandle != null &&
                product.ShopifyStatus == "ACTIVE",
            cancellationToken))
        {
            throw new ValidationException(
                "Choose an active Shopify product.");
        }

        if (await dbContext.SmartSearchMerchandisingRules.AnyAsync(
            rule =>
                rule.NormalizedQueryPhrase == normalizedQuery &&
                rule.ProductId == productId,
            cancellationToken))
        {
            throw new ValidationException(
                "This product already has a merchandising rule for that search.");
        }

        var rule = new SmartSearchMerchandisingRule
        {
            QueryPhrase = queryPhrase!.Trim(),
            NormalizedQueryPhrase = normalizedQuery,
            ProductId = productId,
            RuleType = normalizedRuleType,
            CreatedByUserId = userId
        };
        dbContext.SmartSearchMerchandisingRules.Add(rule);
        await dbContext.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<bool> SetActiveAsync(
        Guid ruleId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await dbContext.SmartSearchMerchandisingRules
            .SingleOrDefaultAsync(
                item => item.Id == ruleId,
                cancellationToken);
        if (rule is null)
        {
            return false;
        }

        rule.IsActive = isActive;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static string NormalizeRuleType(string? ruleType) =>
        ruleType?.Trim() switch
        {
            SmartSearchMerchandisingRuleTypes.Pin =>
                SmartSearchMerchandisingRuleTypes.Pin,
            SmartSearchMerchandisingRuleTypes.Boost =>
                SmartSearchMerchandisingRuleTypes.Boost,
            _ => throw new ValidationException(
                "Choose Pin to top or Boost ranking.")
        };
}
