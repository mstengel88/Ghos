using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;

namespace Ghos.Web.Products;

public sealed class ProductFormModel
{
    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(80)]
    [Display(Name = "Product code / SKU")]
    public string? ProductCode { get; set; }

    [Required]
    [Display(Name = "Category")]
    public Guid? ProductCategoryId { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    [StringLength(320)]
    [Display(Name = "Short description")]
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    [Display(Name = "Best uses")]
    public string? BestUses { get; set; }

    public string? Limitations { get; set; }

    [Display(Name = "Alternate names and search synonyms")]
    public string? AlternateNames { get; set; }

    [Display(Name = "Pickup")]
    public bool AvailableForPickup { get; set; } = true;

    [Display(Name = "Delivery")]
    public bool AvailableForDelivery { get; set; } = true;

    [Display(Name = "Bulk")]
    public bool AvailableInBulk { get; set; } = true;

    [Display(Name = "Bagged")]
    public bool AvailableBagged { get; set; }
}
