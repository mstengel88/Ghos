using Ghos.Web.Data;
using Ghos.Web.Dispatch;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class DispatchSyncServiceTests
{
    [Fact]
    public void ReconcileMissingOpenDeliveries_ClosesMissingOpenMirror()
    {
        var synchronizedAt = new DateTime(
            2026,
            8,
            5,
            21,
            0,
            0,
            DateTimeKind.Utc);
        var order = new SalesOrder
        {
            Status = SalesOrderStatus.New
        };
        var delivery = new Delivery
        {
            ExternalDispatchId = "D-STALE",
            Status = DeliveryStatus.Unscheduled,
            SalesOrder = order
        };

        var count =
            DispatchSyncService.ReconcileMissingOpenDeliveries(
                [delivery],
                ["D-CURRENT"],
                synchronizedAt,
                "admin");

        Assert.Equal(1, count);
        Assert.Equal(DeliveryStatus.Cancelled, delivery.Status);
        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);
        Assert.Equal(
            DispatchSyncService.MissingFromDispatchNote,
            delivery.ReconciliationNote);
        Assert.Equal(synchronizedAt, delivery.ReconciledAtUtc);
    }

    [Fact]
    public void ReconcileMissingOpenDeliveries_PreservesCurrentOpenMirror()
    {
        var delivery = new Delivery
        {
            ExternalDispatchId = "D-CURRENT",
            Status = DeliveryStatus.Unscheduled,
            SalesOrder = new SalesOrder
            {
                Status = SalesOrderStatus.New
            }
        };

        var count =
            DispatchSyncService.ReconcileMissingOpenDeliveries(
                [delivery],
                ["d-current"],
                DateTime.UtcNow,
                "admin");

        Assert.Equal(0, count);
        Assert.Equal(
            DeliveryStatus.Unscheduled,
            delivery.Status);
        Assert.Equal(
            SalesOrderStatus.New,
            delivery.SalesOrder.Status);
    }

    [Fact]
    public void ReconcileMissingOpenDeliveries_PreservesDeliveredHistory()
    {
        var order = new SalesOrder
        {
            Status = SalesOrderStatus.Delivered
        };
        var delivery = new Delivery
        {
            ExternalDispatchId = "S-HISTORY",
            Status = DeliveryStatus.Delivered,
            SalesOrder = order
        };

        var count =
            DispatchSyncService.ReconcileMissingOpenDeliveries(
                [delivery],
                [],
                DateTime.UtcNow,
                "admin");

        Assert.Equal(0, count);
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(SalesOrderStatus.Delivered, order.Status);
    }
}
