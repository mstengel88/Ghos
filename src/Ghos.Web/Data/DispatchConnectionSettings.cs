using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class DispatchConnectionSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string EncryptedIntegrationSecret { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? LastCursor { get; set; }

    public DateTime? LastSyncStartedAtUtc { get; set; }

    public DateTime? LastSyncCompletedAtUtc { get; set; }

    public DateTime? LastSuccessfulSyncAtUtc { get; set; }

    [MaxLength(24)]
    public string? LastSyncStatus { get; set; }

    [MaxLength(1000)]
    public string? LastSyncMessage { get; set; }

    public int LastImportedCount { get; set; }

    public int LastCreatedCount { get; set; }

    public int LastUpdatedCount { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}
