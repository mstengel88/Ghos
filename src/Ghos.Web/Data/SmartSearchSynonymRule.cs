using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class SmartSearchSynonymRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public string Phrase { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NormalizedPhrase { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Expansion { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NormalizedExpansion { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
}
