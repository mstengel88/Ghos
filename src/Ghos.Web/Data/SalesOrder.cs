using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class SalesOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string ExternalKey { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? ExternalOrderId { get; set; }

    [MaxLength(80)]
    public string OrderNumber { get; set; } = string.Empty;

    public SalesOrderSource Source { get; set; } =
        SalesOrderSource.Dispatch;

    public SalesOrderStatus Status { get; set; } =
        SalesOrderStatus.New;

    [MaxLength(180)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(180)]
    public string? Contact { get; set; }

    [MaxLength(220)]
    public string DeliveryAddress { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DeliveryCity { get; set; }

    [MaxLength(24)]
    public string? DeliveryState { get; set; }

    [MaxLength(20)]
    public string? DeliveryPostalCode { get; set; }

    [MaxLength(180)]
    public string? RequestedWindow { get; set; }

    [MaxLength(80)]
    public string? TimePreference { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime? SourceCreatedAtUtc { get; set; }

    public DateTime? SourceUpdatedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public ICollection<Delivery> Deliveries { get; set; } = [];
}
