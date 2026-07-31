using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class WinterWatchConnectionSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(500)]
    public string FunctionUrl { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string EncryptedIntegrationSecret { get; set; } = string.Empty;

    [MaxLength(500)]
    public string InviteRedirectUrl { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}
