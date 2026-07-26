using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class ShopifySyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(24)]
    public string Status { get; set; } = "Running";

    public int ShopifyProductCount { get; set; }

    public int CreatedCount { get; set; }

    public int UpdatedCount { get; set; }

    public int UnchangedCount { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    [MaxLength(450)]
    public string? InitiatedByUserId { get; set; }
}
