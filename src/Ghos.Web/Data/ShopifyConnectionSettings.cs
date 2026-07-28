using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class ShopifyConnectionSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(255)]
    public string EncryptedClientId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string EncryptedClientSecret { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? EncryptedDraftOrderClientId { get; set; }

    [MaxLength(1000)]
    public string? EncryptedDraftOrderClientSecret { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? DraftOrderUpdatedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DraftOrderUpdatedByUserId { get; set; }
}
