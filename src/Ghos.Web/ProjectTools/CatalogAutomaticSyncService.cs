using System.Security.Claims;

namespace Ghos.Web.ProjectTools;

public sealed class CatalogAutomaticSyncService(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogAutomaticSyncService> logger)
    : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(InitialDelay, stoppingToken);
        using var timer = new PeriodicTimer(CheckInterval);

        do
        {
            await RefreshIfStaleAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshIfStaleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var shopify = scope.ServiceProvider
                .GetRequiredService<Shopify.ShopifySyncService>();
            var dispatch = scope.ServiceProvider
                .GetRequiredService<DispatchQuoteDataSyncService>();
            var systemUser = new ClaimsPrincipal(new ClaimsIdentity());

            var shopifyResult = await shopify.SynchronizeIfStaleAsync(
                systemUser,
                cancellationToken: cancellationToken);
            var dispatchResult = await dispatch.SynchronizeIfStaleAsync(
                cancellationToken);

            if (shopifyResult is not null || dispatchResult is not null)
            {
                logger.LogInformation(
                    "Background catalog refresh completed. Shopify: {Shopify}; Dispatch quote data: {Dispatch}.",
                    shopifyResult is null ? "current" : "refreshed",
                    dispatchResult is null ? "current" : "refreshed");
            }
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
                "Background catalog refresh failed. GHOS retained the last successful local snapshot.");
        }
    }
}
