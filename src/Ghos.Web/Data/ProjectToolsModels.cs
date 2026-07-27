using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public enum MaterialSoldBy
{
    CubicYard,
    Ton,
    Unit
}

public sealed class ProductMaterialProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public MaterialSoldBy SoldBy { get; set; } = MaterialSoldBy.CubicYard;

    public decimal? TonsPerCubicYard { get; set; }

    public decimal OrderIncrement { get; set; } = 1m;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}

public enum QuoteStatus
{
    Draft,
    ReadyForReview,
    Sent,
    Accepted,
    Declined,
    Expired,
    Converted
}

public sealed class CustomerQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(40)]
    public string QuoteNumber { get; set; } = string.Empty;

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    [MaxLength(160)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? CompanyName { get; set; }

    [MaxLength(240)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(240)]
    public string? AddressLine1 { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(40)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public bool IsContractor { get; set; }

    public bool IsTaxExempt { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DeliveryAmount { get; set; }

    public decimal TaxRate { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    public string? InternalNotes { get; set; }

    public string? CustomerNotes { get; set; }

    public DateTime? ValidUntilUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    [MaxLength(120)]
    public string? LegacyExternalId { get; set; }

    [MaxLength(120)]
    public string? ShopifyDraftOrderId { get; set; }

    public ICollection<CustomerQuoteLine> Lines { get; set; } = [];
}

public sealed class CustomerQuoteLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CustomerQuoteId { get; set; }

    public CustomerQuote CustomerQuote { get; set; } = null!;

    public Guid? ProductId { get; set; }

    public Product? Product { get; set; }

    public Guid? ProductVariantId { get; set; }

    public ProductVariant? ProductVariant { get; set; }

    [MaxLength(180)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Sku { get; set; }

    [MaxLength(40)]
    public string UnitLabel { get; set; } = "unit";

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}
