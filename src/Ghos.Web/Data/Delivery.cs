using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class Delivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SalesOrderId { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;

    [MaxLength(100)]
    public string ExternalDispatchId { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ExternalRouteId { get; set; }

    [MaxLength(80)]
    public string? RouteCode { get; set; }

    [MaxLength(80)]
    public string? Truck { get; set; }

    [MaxLength(160)]
    public string? DriverName { get; set; }

    public int? StopSequence { get; set; }

    [MaxLength(200)]
    public string Material { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Quantity { get; set; }

    [MaxLength(40)]
    public string? Unit { get; set; }

    public DeliveryStatus Status { get; set; } =
        DeliveryStatus.Unscheduled;

    public DateTime? ScheduledForUtc { get; set; }

    [MaxLength(80)]
    public string? Eta { get; set; }

    public decimal? TravelMinutes { get; set; }

    public decimal? TravelMiles { get; set; }

    public DateTime? DepartedAtUtc { get; set; }

    public DateTime? ArrivedAtUtc { get; set; }

    public DateTime? DeliveredAtUtc { get; set; }

    [MaxLength(180)]
    public string? ProofName { get; set; }

    [MaxLength(2000)]
    public string? ProofNotes { get; set; }

    public int ProofPhotoCount { get; set; }

    public DateTime? SourceCreatedAtUtc { get; set; }

    public DateTime? SourceUpdatedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
