namespace Ghos.Web.WebsiteHealth;

public sealed class WebsiteHealthRunCoordinator(
    IServiceScopeFactory scopeFactory,
    ILogger<WebsiteHealthRunCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning => _gate.CurrentCount == 0;

    public async Task<WebsiteCheckRun?> TryRunAsync(
        Guid siteId,
        string trigger,
        string? requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return null;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var monitor = scope.ServiceProvider
                .GetRequiredService<WebsiteHealthMonitorService>();
            return await monitor.RunAsync(
                siteId,
                trigger,
                requestedByUserId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unable to execute website health run for site {SiteId}.",
                siteId);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
