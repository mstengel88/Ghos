using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.WebsiteHealth;

public sealed class WebsiteHealthSettingsService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory)
{
    public async Task SaveSiteAsync(
        WebsiteHealthSiteSettingsInput input,
        CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(
            input,
            new ValidationContext(input),
            validateAllProperties: true);
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var site = await dbContext.MonitoredSites.SingleAsync(
            item => item.Id == input.SiteId,
            cancellationToken);
        site.IsEnabled = input.IsEnabled;
        site.CheckIntervalMinutes = input.CheckIntervalMinutes;
        site.RequestTimeoutSeconds = input.RequestTimeoutSeconds;
        site.RequestDelayMilliseconds = input.RequestDelayMilliseconds;
        site.MaxCrawlPages = input.MaxCrawlPages;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetCheckEnabledAsync(
        Guid siteId,
        Guid checkId,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var check = await dbContext.WebsiteChecks.SingleAsync(
            item => item.Id == checkId && item.MonitoredSiteId == siteId,
            cancellationToken);
        if (check.Key == "homepage" && !isEnabled)
        {
            throw new ValidationException(
                "Homepage availability is the required foundation check.");
        }

        check.IsEnabled = isEnabled;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddKeyPageAsync(
        Guid siteId,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeKeyPagePath(targetPath);
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await dbContext.WebsiteChecks.AnyAsync(
            item =>
                item.MonitoredSiteId == siteId &&
                item.Key == "key-page" &&
                item.TargetPath == normalizedPath,
            cancellationToken);
        if (exists)
        {
            throw new ValidationException(
                "That key page is already monitored.");
        }

        dbContext.WebsiteChecks.Add(new WebsiteCheck
        {
            MonitoredSiteId = siteId,
            Key = "key-page",
            DisplayName = $"Key page: {normalizedPath}",
            Category = "Availability",
            TargetPath = normalizedPath,
            Weight = 5
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveKeyPageAsync(
        Guid siteId,
        Guid checkId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var check = await dbContext.WebsiteChecks.SingleAsync(
            item =>
                item.Id == checkId &&
                item.MonitoredSiteId == siteId &&
                item.Key == "key-page",
            cancellationToken);
        dbContext.WebsiteChecks.Remove(check);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    internal static string NormalizeKeyPagePath(string targetPath)
    {
        var normalized = targetPath.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 500 ||
            !normalized.StartsWith('/') ||
            normalized.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ValidationException(
                "Enter a relative site path beginning with one slash, such as /collections/all.");
        }

        return normalized;
    }
}

public sealed class WebsiteHealthSiteSettingsInput
{
    public Guid SiteId { get; set; }

    public bool IsEnabled { get; set; }

    [Range(15, 1440)]
    public int CheckIntervalMinutes { get; set; }

    [Range(5, 60)]
    public int RequestTimeoutSeconds { get; set; }

    [Range(100, 5000)]
    public int RequestDelayMilliseconds { get; set; }

    [Range(5, 100)]
    public int MaxCrawlPages { get; set; }
}
