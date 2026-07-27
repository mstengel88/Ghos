using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class ProductVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [MaxLength(80)]
    public string ShopifyVariantId { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Sku { get; set; }

    [MaxLength(100)]
    public string? Barcode { get; set; }

    public decimal Price { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public decimal? ContractorTier1Price { get; set; }

    public decimal? ContractorTier2Price { get; set; }

    [MaxLength(40)]
    public string? UnitLabel { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    public bool AvailableForSale { get; set; }

    public int SortOrder { get; set; }
}
