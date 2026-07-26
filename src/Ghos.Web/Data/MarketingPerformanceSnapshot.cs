using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class MarketingPerformanceSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MarketingContentPackageId { get; set; }

    public MarketingContentPackage MarketingContentPackage { get; set; } =
        null!;

    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

    public int? FacebookReach { get; set; }

    public int? FacebookEngagements { get; set; }

    public int? InstagramReach { get; set; }

    public int? InstagramEngagements { get; set; }

    public int? WebsiteClicks { get; set; }

    public int? Leads { get; set; }

    public int? Orders { get; set; }

    public decimal? Revenue { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
