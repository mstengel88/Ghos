using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ghos.Web.WebsiteHealth;

public sealed class WebsiteHealthAutomaticCheckService(
    IServiceScopeFactory scopeFactory,
    WebsiteHealthRunCoordinator coordinator,
    IOptions<WebsiteHealthOptions> options,
    ILogger<WebsiteHealthAutomaticCheckService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.SchedulerEnabled)
        {
            logger.LogInformation(
                "Website Health scheduler is disabled. Manual runs remain available.");
            return;
        }

        await Task.Delay(
            TimeSpan.FromSeconds(Math.Max(settings.InitialDelaySeconds, 5)),
            stoppingToken);
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(Math.Max(settings.SchedulerPollMinutes, 1)));

        do
        {
            try
            {
                await RunDueSitesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Website Health scheduler poll failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunDueSitesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var dbContext =
            await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var sites = await dbContext.MonitoredSites
            .AsNoTracking()
            .Where(site => site.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var site in sites.Where(site =>
            site.LastCheckedAtUtc is null ||
            site.LastCheckedAtUtc <= now.AddMinutes(-site.CheckIntervalMinutes)))
        {
            await coordinator.TryRunAsync(
                site.Id,
                "Scheduled",
                null,
                cancellationToken);
        }
    }
}
