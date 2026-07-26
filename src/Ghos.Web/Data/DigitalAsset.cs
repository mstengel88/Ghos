using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class DigitalAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string RelativePath { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ContentType { get; set; } = "application/octet-stream";

    public AssetKind Kind { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.PendingReview;

    public AssetSource Source { get; set; } = AssetSource.Upload;

    [MaxLength(2048)]
    public string? SourceUrl { get; set; }

    public long FileSizeBytes { get; set; }

    [MaxLength(64)]
    public string Sha256Hash { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(2000)]
    public string? Tags { get; set; }

    [Range(0, 5)]
    public int Rating { get; set; }

    public DateTime? CapturedAtUtc { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public ICollection<AssetProductLink> ProductLinks { get; set; } = [];
}
