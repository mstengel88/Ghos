using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class MarketingContentPackage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(180)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Series { get; set; } = string.Empty;

    [MaxLength(80)]
    public string TemplateKey { get; set; } = string.Empty;

    public MarketingContentStatus Status { get; set; } =
        MarketingContentStatus.Draft;

    public DateTime? ScheduledForUtc { get; set; }

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public Guid? DigitalAssetId { get; set; }

    public DigitalAsset? DigitalAsset { get; set; }

    [MaxLength(120)]
    public string Headline { get; set; } = string.Empty;

    [MaxLength(220)]
    public string? Subheadline { get; set; }

    [MaxLength(160)]
    public string? AlternateName { get; set; }

    [MaxLength(1200)]
    public string? FactItems { get; set; }

    [MaxLength(4000)]
    public string FacebookCaption { get; set; } = string.Empty;

    [MaxLength(2200)]
    public string InstagramCaption { get; set; } = string.Empty;

    [MaxLength(600)]
    public string StoryPrompt { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string ReelScript { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Hashtags { get; set; }

    [MaxLength(220)]
    public string CallToAction { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? DestinationUrl { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ApprovedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}
