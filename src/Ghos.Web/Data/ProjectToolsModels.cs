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

public enum QuoteAudience
{
    Customer,
    Contractor,
    Custom
}

public enum ContractorTier
{
    Tier1,
    Tier2
}

public sealed class CustomerQuote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(40)]
    public string QuoteNumber { get; set; } = string.Empty;

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public QuoteAudience Audience { get; set; } = QuoteAudience.Customer;

    public ContractorTier ContractorTier { get; set; } = ContractorTier.Tier1;

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

    [MaxLength(240)]
    public string? AddressLine2 { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(40)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    public bool IsContractor { get; set; }

    public bool IsTaxExempt { get; set; }

    [MaxLength(120)]
    public string? ShopifyCompanyId { get; set; }

    [MaxLength(120)]
    public string? ShopifyCompanyContactId { get; set; }

    [MaxLength(120)]
    public string? ShopifyCompanyLocationId { get; set; }

    [MaxLength(160)]
    public string? PaymentTermsName { get; set; }

    [MaxLength(120)]
    public string? PaymentTermsTemplateId { get; set; }

    public int? PaymentTermsDueInDays { get; set; }

    [MaxLength(240)]
    public string? BillingAddressLine1 { get; set; }

    [MaxLength(240)]
    public string? BillingAddressLine2 { get; set; }

    [MaxLength(120)]
    public string? BillingCity { get; set; }

    [MaxLength(40)]
    public string? BillingState { get; set; }

    [MaxLength(20)]
    public string? BillingPostalCode { get; set; }

    [MaxLength(8)]
    public string? BillingCountry { get; set; }

    public decimal Subtotal { get; set; }

    public decimal DeliveryAmount { get; set; }

    public decimal? CalculatedDeliveryAmount { get; set; }

    public decimal? CustomDeliveryAmount { get; set; }

    public decimal? RatePerMinute { get; set; }

    public decimal? ShippingQuantity { get; set; }

    public decimal? ShippingRate { get; set; }

    [MaxLength(40)]
    public string? ShippingUnit { get; set; }

    public decimal TaxRate { get; set; }

    [MaxLength(80)]
    public string? TaxRateLabel { get; set; }

    public decimal TaxAmount { get; set; }

    public decimal Total { get; set; }

    [MaxLength(160)]
    public string? DeliveryServiceName { get; set; }

    [MaxLength(240)]
    public string? DeliveryDescription { get; set; }

    [MaxLength(80)]
    public string? DeliveryEta { get; set; }

    [MaxLength(240)]
    public string? DeliverySummary { get; set; }

    public string? SourceBreakdownJson { get; set; }

    public string? InternalNotes { get; set; }

    public string? CustomerNotes { get; set; }

    public DateTime? ValidUntilUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    [MaxLength(450)]
    public string? DeletedByUserId { get; set; }

    [MaxLength(120)]
    public string? LegacyExternalId { get; set; }

    [MaxLength(120)]
    public string? ShopifyDraftOrderId { get; set; }

    [MaxLength(500)]
    public string? ShopifyDraftOrderUrl { get; set; }

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

    [MaxLength(160)]
    public string? Vendor { get; set; }

    [MaxLength(180)]
    public string? ProductHandle { get; set; }

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    [MaxLength(120)]
    public string? ShopifyVariantIdSnapshot { get; set; }

    [MaxLength(40)]
    public string UnitLabel { get; set; } = "unit";

    [MaxLength(40)]
    public string PricingLabel { get; set; } = "Customer";

    public QuoteAudience Audience { get; set; } = QuoteAudience.Customer;

    public ContractorTier? ContractorTier { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public int SortOrder { get; set; }
}

public sealed class QuoteConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool EnableCalculatedRates { get; set; } = true;

    public bool UseTestFlatRate { get; set; }

    public decimal TestFlatRate { get; set; } = 50m;

    public bool EnableRemoteSurcharge { get; set; } = true;

    public bool ShowVendorSource { get; set; } = true;

    public decimal DefaultTaxRate { get; set; } = .055m;

    public decimal DefaultRatePerMinute { get; set; } = 2.08m;

    public decimal MaximumDeliveryRadiusMiles { get; set; } = 50m;

    [MaxLength(40)]
    public string OutsideRadiusPhone { get; set; } = "(262) 345-4001";

    [MaxLength(160)]
    public string DefaultOriginLabel { get; set; } = "Menomonee Falls";

    [MaxLength(300)]
    public string DefaultOriginAddress { get; set; } =
        "W185 N7487 Narrow Ln, Menomonee Falls, WI 53051";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DispatchDataLastSyncedAtUtc { get; set; }

    public int DispatchDataLastProductCount { get; set; }

    public int DispatchDataLastCompanyCount { get; set; }

    public int DispatchDataLastQuoteCount { get; set; }

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}

public sealed class QuoteTaxRateCache
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string CacheKey { get; set; } = string.Empty;

    [MaxLength(240)]
    public string? AddressLine1 { get; set; }

    [MaxLength(120)]
    public string? City { get; set; }

    [MaxLength(40)]
    public string? State { get; set; }

    [MaxLength(20)]
    public string? PostalCode { get; set; }

    [MaxLength(8)]
    public string? Country { get; set; }

    public decimal Rate { get; set; }

    [MaxLength(80)]
    public string Label { get; set; } = "Shopify tax";

    [MaxLength(40)]
    public string Source { get; set; } = "shopify";

    public decimal SampleTaxableAmount { get; set; } = 100m;

    public decimal? ShopifyTotalTax { get; set; }

    public DateTime CalculatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class QuoteB2BCompany
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public string ExternalId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ShopifyCompanyId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? ShopifyCompanyContactId { get; set; }

    [MaxLength(120)]
    public string? ShopifyCompanyLocationId { get; set; }

    [MaxLength(160)]
    public string CompanyName { get; set; } = string.Empty;

    public ContractorTier ContractorTier { get; set; } = ContractorTier.Tier1;

    public string? CatalogTitles { get; set; }

    [MaxLength(160)]
    public string? ContactName { get; set; }

    [MaxLength(240)]
    public string? Email { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(240)]
    public string? BillingAddressLine1 { get; set; }

    [MaxLength(240)]
    public string? BillingAddressLine2 { get; set; }

    [MaxLength(120)]
    public string? BillingCity { get; set; }

    [MaxLength(40)]
    public string? BillingState { get; set; }

    [MaxLength(20)]
    public string? BillingPostalCode { get; set; }

    [MaxLength(8)]
    public string? BillingCountry { get; set; }

    public bool IsTaxExempt { get; set; }

    [MaxLength(160)]
    public string? PaymentTermsName { get; set; }

    [MaxLength(120)]
    public string? PaymentTermsTemplateId { get; set; }

    public int? PaymentTermsDueInDays { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class QuoteMaterialRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(12)]
    public string SkuPrefix { get; set; } = string.Empty;

    [MaxLength(80)]
    public string MaterialName { get; set; } = string.Empty;

    public decimal TruckCapacity { get; set; } = 22m;

    [MaxLength(16)]
    public string DeliveryMode { get; set; } = "bulk";

    [MaxLength(16)]
    public string CapacityUnit { get; set; } = "quantity";

    [MaxLength(160)]
    public string? VendorSource { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }
}

public sealed class QuoteOriginAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(300)]
    public string Address { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}
