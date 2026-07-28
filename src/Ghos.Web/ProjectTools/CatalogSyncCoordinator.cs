namespace Ghos.Web.ProjectTools;

/// <summary>
/// Serializes writers that share the GHOS product and variant catalog.
/// Shopify owns storefront fields while Dispatch owns quote-specific fields,
/// but both integrations update the same database rows.
/// </summary>
public sealed class CatalogSyncCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
