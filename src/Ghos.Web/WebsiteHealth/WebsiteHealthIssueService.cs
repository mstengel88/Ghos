using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.WebsiteHealth;

public enum WebsiteHealthIssueAction
{
    Acknowledge,
    ClearAcknowledgement,
    Suppress,
    Restore
}

public sealed class WebsiteHealthIssueService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task<bool> SaveReviewedValueAsync(
        Guid issueId,
        string userId,
        string? value,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var issue = await dbContext.WebsiteHealthIssues
            .SingleOrDefaultAsync(
                item => item.Id == issueId,
                cancellationToken);
        if (issue is null)
        {
            return false;
        }

        var normalized = NormalizeReviewedValue(value);
        if (normalized is null)
        {
            return false;
        }

        issue.ReviewedValue = normalized;
        issue.ReviewedAtUtc = DateTime.UtcNow;
        issue.ReviewedByUserId = userId;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ClearReviewedValueAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var issue = await dbContext.WebsiteHealthIssues
            .SingleOrDefaultAsync(
                item => item.Id == issueId,
                cancellationToken);
        if (issue is null)
        {
            return false;
        }

        issue.ReviewedValue = null;
        issue.ReviewedAtUtc = null;
        issue.ReviewedByUserId = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAsync(
        Guid issueId,
        WebsiteHealthIssueAction action,
        string userId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var issue = await dbContext.WebsiteHealthIssues
            .SingleOrDefaultAsync(
                item => item.Id == issueId,
                cancellationToken);
        if (issue is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        switch (action)
        {
            case WebsiteHealthIssueAction.Acknowledge:
                issue.AcknowledgedAtUtc = now;
                issue.AcknowledgedByUserId = userId;
                break;
            case WebsiteHealthIssueAction.ClearAcknowledgement:
                issue.AcknowledgedAtUtc = null;
                issue.AcknowledgedByUserId = null;
                break;
            case WebsiteHealthIssueAction.Suppress:
                issue.SuppressedAtUtc = now;
                issue.SuppressedByUserId = userId;
                issue.AcknowledgedAtUtc ??= now;
                issue.AcknowledgedByUserId ??= userId;
                break;
            case WebsiteHealthIssueAction.Restore:
                issue.SuppressedAtUtc = null;
                issue.SuppressedByUserId = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(action),
                    action,
                    "Unknown issue action.");
        }

        issue.TriageNote = NormalizeNote(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 1000
            ? normalized
            : normalized[..1000];
    }

    internal static string? NormalizeReviewedValue(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= 6000
            ? normalized
            : normalized[..6000];
    }
}
