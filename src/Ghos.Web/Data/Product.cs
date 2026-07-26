using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? ProductCode { get; set; }

    [MaxLength(80)]
    public string? ShopifyProductId { get; set; }

    [MaxLength(160)]
    public string? ShopifyTitle { get; set; }

    [MaxLength(180)]
    public string? ShopifyHandle { get; set; }

    [MaxLength(32)]
    public string? ShopifyStatus { get; set; }

    [MaxLength(160)]
    public string? ShopifyVendor { get; set; }

    [MaxLength(160)]
    public string? ShopifyProductType { get; set; }

    public string? ShopifyDescriptionHtml { get; set; }

    public string? ShopifyTags { get; set; }

    [MaxLength(2048)]
    public string? ShopifyFeaturedImageUrl { get; set; }

    [MaxLength(500)]
    public string? ShopifyFeaturedImageAlt { get; set; }

    [MaxLength(200)]
    public string? ShopifySeoTitle { get; set; }

    [MaxLength(500)]
    public string? ShopifySeoDescription { get; set; }

    public DateTime? ShopifyCreatedAtUtc { get; set; }

    public DateTime? ShopifyUpdatedAtUtc { get; set; }

    public DateTime? ShopifyPublishedAtUtc { get; set; }

    public DateTime? ShopifyLastSyncedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    public Guid ProductCategoryId { get; set; }

    public ProductCategory ProductCategory { get; set; } = null!;

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    [MaxLength(320)]
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public string? BestUses { get; set; }

    public string? Limitations { get; set; }

    public bool AvailableForPickup { get; set; } = true;

    public bool AvailableForDelivery { get; set; } = true;

    public bool AvailableInBulk { get; set; } = true;

    public bool AvailableBagged { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public ICollection<ProductAlternateName> AlternateNames { get; set; } = [];

    public ICollection<ProductVariant> Variants { get; set; } = [];
}
