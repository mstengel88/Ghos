using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class SmartSearchEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(300)]
    public string Query { get; set; } = string.Empty;

    [MaxLength(300)]
    public string NormalizedQuery { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? IntentSummary { get; set; }

    [MaxLength(32)]
    public string Source { get; set; } = "Storefront";

    public int ResultCount { get; set; }

    public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? SelectedProductId { get; set; }

    public DateTime? SelectedAtUtc { get; set; }
}
