using Microsoft.Extensions.Options;

namespace Ghos.Web.Dispatch;

public sealed class DispatchAutomaticSyncService(
    IServiceScopeFactory scopeFactory,
    IOptions<DispatchSyncOptions> options,
    ILogger<DispatchAutomaticSyncService> logger)
    : BackgroundService
{
    private readonly DispatchSyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation(
                "Automatic Dispatch synchronization is disabled.");
            return;
        }

        var initialDelay = TimeSpan.FromSeconds(
            Math.Clamp(
                _options.InitialDelaySeconds,
                5,
                600));
        await Task.Delay(initialDelay, stoppingToken);

        var interval = TimeSpan.FromMinutes(
            Math.Clamp(
                _options.IntervalMinutes,
                1,
                60));
        using var timer = new PeriodicTimer(interval);

        do
        {
            await SynchronizeAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SynchronizeAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope =
                scopeFactory.CreateAsyncScope();
            var syncService =
                scope.ServiceProvider
                    .GetRequiredService<DispatchSyncService>();
            if (!await syncService.IsConfiguredAsync(
                    cancellationToken))
            {
                return;
            }

            var result = await syncService.SynchronizeAsync(
                user: null,
                cancellationToken: cancellationToken);
            logger.LogInformation(
                "Automatic Dispatch synchronization checked {Count} record(s).",
                result.Received);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Automatic Dispatch synchronization failed. Existing GHOS data was retained.");
        }
    }
}
