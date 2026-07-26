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
}
