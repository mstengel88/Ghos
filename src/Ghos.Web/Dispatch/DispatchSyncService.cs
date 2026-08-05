using System.Globalization;
using System.Security.Claims;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.Dispatch;

public sealed record DispatchSyncResult(
    int Received,
    int Created,
    int Updated,
    bool HasMore,
    DateTime CompletedAtUtc);

public sealed class DispatchSyncService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    DispatchCredentialStore credentialStore,
    DispatchIntegrationClient integrationClient,
    ILogger<DispatchSyncService> logger)
{
    internal const string MissingFromDispatchNote =
        "Closed automatically because this order is no longer open in Dispatch.";

    private static readonly SemaphoreSlim SynchronizationLock =
        new(1, 1);

    public async Task<bool> IsConfiguredAsync(
        CancellationToken cancellationToken = default) =>
        await credentialStore.HasCredentialsAsync(cancellationToken);

    public async Task<DispatchConnectionSettings?> GetSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.DispatchConnectionSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    public Task<DispatchExportEnvelope> TestAsync(
        string baseUrl,
        string integrationSecret,
        CancellationToken cancellationToken = default) =>
        integrationClient.FetchAsync(
            baseUrl,
            integrationSecret,
            limit: 1,
            cancellationToken: cancellationToken);

    public async Task<DispatchSyncResult> SynchronizeAsync(
        ClaimsPrincipal? user,
        bool fullRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await SynchronizationLock.WaitAsync(cancellationToken);
        try
        {
            return await SynchronizeCoreAsync(
                user,
                fullRefresh,
                cancellationToken);
        }
        finally
        {
            SynchronizationLock.Release();
        }
    }

    private async Task<DispatchSyncResult> SynchronizeCoreAsync(
        ClaimsPrincipal? user,
        bool fullRefresh,
        CancellationToken cancellationToken)
    {
        var credentials = await credentialStore.GetAsync(cancellationToken)
            ?? throw new DispatchConnectionException(
                "Configure the dispatch connection before synchronizing.");

        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await dbContext.DispatchConnectionSettings
            .SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new DispatchConnectionSettings
            {
                BaseUrl = credentials.BaseUrl,
                EncryptedIntegrationSecret = string.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            };
            dbContext.DispatchConnectionSettings.Add(settings);
        }
        else if (!string.Equals(
                     settings.BaseUrl,
                     credentials.BaseUrl,
                     StringComparison.OrdinalIgnoreCase))
        {
            settings.BaseUrl = credentials.BaseUrl;
            settings.UpdatedAtUtc = DateTime.UtcNow;
        }

        var startedAt = DateTime.UtcNow;
        settings.LastSyncStartedAtUtc = startedAt;
        settings.LastSyncStatus = "Running";
        settings.LastSyncMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var payload = await integrationClient.FetchAsync(
                credentials.BaseUrl,
                credentials.IntegrationSecret,
                fullRefresh ? null : settings.LastCursor,
                cancellationToken: cancellationToken);
            var routes = payload.Routes.ToDictionary(
                route => route.Id,
                StringComparer.OrdinalIgnoreCase);
            var externalKeys = payload.Orders
                .Select(order => GetExternalKey(order.Id))
                .ToList();
            var externalDeliveryIds = payload.Orders
                .Select(order => order.Id)
                .ToList();
            var existingOrders = await dbContext.SalesOrders
                .Where(order => externalKeys.Contains(order.ExternalKey))
                .ToDictionaryAsync(
                    order => order.ExternalKey,
                    cancellationToken);
            var existingDeliveries = await dbContext.Deliveries
                .Where(delivery =>
                    externalDeliveryIds.Contains(
                        delivery.ExternalDispatchId))
                .ToDictionaryAsync(
                    delivery => delivery.ExternalDispatchId,
                    cancellationToken);

            var userId = user?.FindFirstValue(
                ClaimTypes.NameIdentifier);
            var synchronizedAt = DateTime.UtcNow;
            var created = 0;
            var updated = 0;

            foreach (var source in payload.Orders)
            {
                var externalKey = GetExternalKey(source.Id);
                var isNewOrder =
                    !existingOrders.TryGetValue(externalKey, out var order);
                if (isNewOrder)
                {
                    order = new SalesOrder
                    {
                        ExternalKey = externalKey,
                        CreatedAtUtc = synchronizedAt,
                        CreatedByUserId = userId
                    };
                    dbContext.SalesOrders.Add(order);
                    existingOrders[externalKey] = order;
                }

                ApplyOrder(order!, source, synchronizedAt, userId);

                if (!existingDeliveries.TryGetValue(
                        source.Id,
                        out var delivery))
                {
                    delivery = new Delivery
                    {
                        ExternalDispatchId = source.Id,
                        SalesOrder = order!,
                        CreatedAtUtc = synchronizedAt
                    };
                    dbContext.Deliveries.Add(delivery);
                    existingDeliveries[source.Id] = delivery;
                    created++;
                }
                else
                {
                    delivery.SalesOrder = order!;
                    updated++;
                }

                routes.TryGetValue(
                    source.AssignedRouteId ?? string.Empty,
                    out var route);
                ApplyDelivery(
                    delivery,
                    source,
                    route,
                    synchronizedAt);
                if (delivery.ReconciledStatusOverride ==
                        DeliveryStatus.Delivered &&
                    order!.Status is not
                        SalesOrderStatus.Delivered and not
                        SalesOrderStatus.Cancelled)
                {
                    order.Status = SalesOrderStatus.Delivered;
                }
            }

            var reconciled = 0;
            if (payload.OpenOrderIds is not null)
            {
                var openDeliveries = await dbContext.Deliveries
                    .Include(delivery => delivery.SalesOrder)
                    .Where(delivery =>
                        delivery.Status != DeliveryStatus.Delivered &&
                        delivery.Status != DeliveryStatus.Cancelled)
                    .ToListAsync(cancellationToken);
                reconciled = ReconcileMissingOpenDeliveries(
                    openDeliveries,
                    payload.OpenOrderIds,
                    synchronizedAt,
                    userId);
                updated += reconciled;
            }

            settings.LastCursor = payload.Cursor;
            settings.LastSyncCompletedAtUtc = synchronizedAt;
            settings.LastSuccessfulSyncAtUtc = synchronizedAt;
            settings.LastSyncStatus = "Succeeded";
            settings.LastImportedCount = payload.Count;
            settings.LastCreatedCount = created;
            settings.LastUpdatedCount = updated;
            var reconciliationMessage = reconciled > 0
                ? $" Closed {reconciled} stale open mirror record{(reconciled == 1 ? string.Empty : "s")}."
                : string.Empty;
            settings.LastSyncMessage = payload.HasMore
                ? "Synchronized the newest 1,000 dispatch records. Run again after current updates are processed." +
                    reconciliationMessage
                : "Dispatch synchronization completed." +
                    reconciliationMessage;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new DispatchSyncResult(
                payload.Count,
                created,
                updated,
                payload.HasMore,
                synchronizedAt);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            settings = await dbContext.DispatchConnectionSettings
                .SingleAsync(cancellationToken);
            settings.LastSyncCompletedAtUtc = DateTime.UtcNow;
            settings.LastSyncStatus = "Failed";
            settings.LastSyncMessage = exception is DispatchConnectionException
                ? exception.Message
                : "Dispatch synchronization failed. Existing GHOS data was retained.";
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogError(
                exception,
                "Dispatch synchronization failed.");
            throw;
        }
    }

    private static string GetExternalKey(string id) => $"dispatch:{id}";

    private static void ApplyOrder(
        SalesOrder target,
        DispatchExportOrder source,
        DateTime synchronizedAt,
        string? userId)
    {
        target.ExternalOrderId = source.Id;
        target.OrderNumber = EmptyFallback(
            source.OrderNumber,
            source.Id);
        target.Source = SalesOrderSource.Dispatch;
        target.Status = MapOrderStatus(source.Status);
        target.CustomerName = EmptyFallback(
            source.Customer,
            "Customer not provided");
        target.Contact = NullIfEmpty(source.Contact);
        target.DeliveryAddress = EmptyFallback(
            source.Address,
            "Address not provided");
        target.DeliveryCity = NullIfEmpty(source.City);
        target.RequestedWindow =
            NullIfEmpty(source.RequestedWindow);
        target.TimePreference =
            NullIfEmpty(source.TimePreference);
        target.SourceCreatedAtUtc =
            ParseTimestamp(source.CreatedAt);
        target.SourceUpdatedAtUtc =
            ParseTimestamp(source.UpdatedAt);
        target.LastSyncedAtUtc = synchronizedAt;
        target.UpdatedAtUtc = synchronizedAt;
        target.UpdatedByUserId = userId;
    }

    private static void ApplyDelivery(
        Delivery target,
        DispatchExportOrder source,
        DispatchExportRoute? route,
        DateTime synchronizedAt)
    {
        target.ExternalRouteId = source.AssignedRouteId;
        target.RouteCode = NullIfEmpty(route?.Code);
        target.Truck = NullIfEmpty(route?.Truck);
        target.DriverName = NullIfEmpty(route?.Driver);
        target.StopSequence = source.StopSequence;
        target.Material = EmptyFallback(
            source.Material,
            "Material not provided");
        target.Quantity = NullIfEmpty(source.Quantity);
        target.Unit = NullIfEmpty(source.Unit);
        var sourceStatus = MapDeliveryStatus(source);
        target.Status =
            sourceStatus is DeliveryStatus.Delivered or
                DeliveryStatus.Cancelled
                ? sourceStatus
                : target.ReconciledStatusOverride ??
                    sourceStatus;
        target.ScheduledForUtc =
            ParseTimestamp(source.RequestedWindow);
        target.Eta = NullIfEmpty(source.Eta);
        target.TravelMinutes = source.TravelMinutes;
        target.TravelMiles = source.TravelMiles;
        target.DepartedAtUtc =
            ParseTimestamp(source.DepartedAt);
        target.DeliveredAtUtc =
            ParseTimestamp(source.DeliveredAt);
        target.ProofName = NullIfEmpty(source.ProofName);
        target.ProofNotes = NullIfEmpty(source.ProofNotes);
        target.SourceCreatedAtUtc =
            ParseTimestamp(source.CreatedAt);
        target.SourceUpdatedAtUtc =
            ParseTimestamp(source.UpdatedAt);
        target.LastSyncedAtUtc = synchronizedAt;
        target.UpdatedAtUtc = synchronizedAt;
        if (string.Equals(
                target.ReconciliationNote,
                MissingFromDispatchNote,
                StringComparison.Ordinal))
        {
            target.ReconciliationNote = null;
            target.ReconciledAtUtc = null;
            target.ReconciledByUserId = null;
        }
    }

    internal static int ReconcileMissingOpenDeliveries(
        IEnumerable<Delivery> deliveries,
        IReadOnlyCollection<string> sourceOpenIds,
        DateTime synchronizedAt,
        string? userId)
    {
        var openIds = sourceOpenIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reconciled = 0;

        foreach (var delivery in deliveries)
        {
            if (delivery.Status is DeliveryStatus.Delivered or
                    DeliveryStatus.Cancelled ||
                string.IsNullOrWhiteSpace(
                    delivery.ExternalDispatchId) ||
                openIds.Contains(delivery.ExternalDispatchId))
            {
                continue;
            }

            delivery.Status = DeliveryStatus.Cancelled;
            delivery.ReconciliationNote =
                MissingFromDispatchNote;
            delivery.ReconciledAtUtc = synchronizedAt;
            delivery.ReconciledByUserId = userId;
            delivery.LastSyncedAtUtc = synchronizedAt;
            delivery.UpdatedAtUtc = synchronizedAt;

            if (delivery.SalesOrder is not null &&
                delivery.SalesOrder.Status is not
                    SalesOrderStatus.Delivered and not
                    SalesOrderStatus.Cancelled)
            {
                delivery.SalesOrder.Status =
                    SalesOrderStatus.Cancelled;
                delivery.SalesOrder.LastSyncedAtUtc =
                    synchronizedAt;
                delivery.SalesOrder.UpdatedAtUtc =
                    synchronizedAt;
                delivery.SalesOrder.UpdatedByUserId =
                    userId;
            }

            reconciled++;
        }

        return reconciled;
    }

    private static SalesOrderStatus MapOrderStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "scheduled" => SalesOrderStatus.Scheduled,
            "hold" => SalesOrderStatus.Hold,
            "delivered" => SalesOrderStatus.Delivered,
            "cancelled" => SalesOrderStatus.Cancelled,
            _ => SalesOrderStatus.New
        };

    private static DeliveryStatus MapDeliveryStatus(
        DispatchExportOrder source)
    {
        if (source.Status.Equals(
                "cancelled",
                StringComparison.OrdinalIgnoreCase))
        {
            return DeliveryStatus.Cancelled;
        }

        if (source.Status.Equals(
                "delivered",
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(source.DeliveredAt) ||
            source.DeliveryStatus.Equals(
                "delivered",
                StringComparison.OrdinalIgnoreCase))
        {
            return DeliveryStatus.Delivered;
        }

        return source.DeliveryStatus.Trim().ToLowerInvariant() switch
        {
            "en_route" when
                DispatchOperationalTime.IsCurrentEnRoute(source) =>
                DeliveryStatus.EnRoute,
            _ when !string.IsNullOrWhiteSpace(
                source.AssignedRouteId) =>
                DeliveryStatus.Scheduled,
            _ => DeliveryStatus.Unscheduled
        };
    }

    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal |
                DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
    }

    private static string EmptyFallback(
        string? value,
        string fallback) =>
        string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
