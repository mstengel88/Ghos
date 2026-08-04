using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.SmartSearch;

public sealed class SmartSearchTuningService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<IReadOnlyList<SmartSearchSynonymRule>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.SmartSearchSynonymRules
            .AsNoTracking()
            .OrderByDescending(rule => rule.IsActive)
            .ThenBy(rule => rule.Phrase)
            .ToListAsync(cancellationToken);
    }

    public async Task<SmartSearchSynonymRule> AddRuleAsync(
        string? phrase,
        string? expansion,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhrase = NormalizeRequired(phrase, "Customer phrase");
        var normalizedExpansion = NormalizeRequired(
            expansion,
            "Search as");
        if (normalizedPhrase == normalizedExpansion)
        {
            throw new ValidationException(
                "The customer phrase and search term must be different.");
        }

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (await dbContext.SmartSearchSynonymRules.AnyAsync(
            rule =>
                rule.NormalizedPhrase == normalizedPhrase &&
                rule.NormalizedExpansion == normalizedExpansion,
            cancellationToken))
        {
            throw new ValidationException(
                "This synonym rule already exists.");
        }

        var rule = new SmartSearchSynonymRule
        {
            Phrase = phrase!.Trim(),
            NormalizedPhrase = normalizedPhrase,
            Expansion = expansion!.Trim(),
            NormalizedExpansion = normalizedExpansion,
            CreatedByUserId = userId
        };
        dbContext.SmartSearchSynonymRules.Add(rule);
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
        var rule = await dbContext.SmartSearchSynonymRules
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

    internal static string NormalizeRequired(
        string? value,
        string label)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length is < 2 or > 120)
        {
            throw new ValidationException(
                $"{label} must be between 2 and 120 characters.");
        }

        var normalized = SmartSearchSynonymLibrary.Normalize(trimmed);
        if (normalized.Length < 2)
        {
            throw new ValidationException(
                $"{label} must include at least two letters or numbers.");
        }

        return normalized;
    }
}
