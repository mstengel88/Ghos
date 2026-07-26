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

    public bool AvailableForSale { get; set; }

    public int SortOrder { get; set; }
}
