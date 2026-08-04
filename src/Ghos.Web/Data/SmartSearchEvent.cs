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

    [MaxLength(200)]
    public string? TopResultTitle { get; set; }

    [MaxLength(16)]
    public string? TopResultConfidence { get; set; }

    [MaxLength(500)]
    public string? UnmatchedIntentSummary { get; set; }

    [MaxLength(300)]
    public string? CorrectedQuery { get; set; }

    [MaxLength(500)]
    public string? CorrectionSummary { get; set; }

    public bool CorrectionApplied { get; set; }

    public DateTime SearchedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? SelectedProductId { get; set; }

    public DateTime? SelectedAtUtc { get; set; }
}
