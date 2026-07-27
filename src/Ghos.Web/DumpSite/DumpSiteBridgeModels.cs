using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ghos.Web.DumpSite;

public sealed class DumpSiteBridgeEntry
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("confirmation_id")]
    public string ConfirmationId { get; set; } = string.Empty;

    [JsonPropertyName("submitted_at")]
    public DateTime SubmittedAtUtc { get; set; }

    [JsonPropertyName("access_source")]
    public string AccessSource { get; set; } = string.Empty;

    [JsonPropertyName("shopify_customer")]
    public JsonElement? ShopifyCustomer { get; set; }

    [JsonPropertyName("shopify_company_id")]
    public string ShopifyCompanyId { get; set; } = string.Empty;

    [JsonPropertyName("company_name")]
    public string CompanyName { get; set; } = string.Empty;

    [JsonPropertyName("truck_number")]
    public string TruckNumber { get; set; } = string.Empty;

    [JsonPropertyName("driver_name")]
    public string DriverName { get; set; } = string.Empty;

    [JsonPropertyName("material_type")]
    public string MaterialType { get; set; } = string.Empty;

    [JsonPropertyName("vehicle_type")]
    public string VehicleType { get; set; } = string.Empty;

    [JsonPropertyName("modern_retail_status")]
    public string ModernRetailStatus { get; set; } = string.Empty;

    [JsonPropertyName("modern_retail_order_number")]
    public string? ModernRetailOrderNumber { get; set; }

    [JsonPropertyName("counterpoint_bridge_status")]
    public string BridgeStatus { get; set; } = string.Empty;

    [JsonPropertyName("claim_token")]
    public Guid? ClaimToken { get; set; }

    [JsonPropertyName("claimed_by_this_operator")]
    public bool ClaimedByThisOperator { get; set; }
}

public sealed class DumpSiteEntriesResponse
{
    [JsonPropertyName("entries")]
    public List<DumpSiteBridgeEntry> Entries { get; set; } = [];
}

public sealed class DumpSiteEntryResponse
{
    [JsonPropertyName("entry")]
    public DumpSiteBridgeEntry? Entry { get; set; }
}

public sealed class DumpSiteHealthResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("time")]
    public DateTime? Time { get; set; }
}

public sealed record DumpSiteQueueRecord(
    Guid Id,
    Guid? ClaimToken,
    bool Claimed,
    string ConfirmationId,
    DateTime SubmittedAtUtc,
    string CompanyName,
    string ShopifyCompanyId,
    string CustomerNumber,
    string SubmittedByName,
    string SubmittedByEmail,
    string TruckNumber,
    string DriverName,
    string MaterialType,
    string VehicleType,
    string Barcode,
    string ItemDescription,
    decimal Quantity,
    decimal UnitPrice,
    decimal Tax,
    decimal Total,
    string Location,
    string Station,
    string Drawer,
    string SalesRep,
    IReadOnlyList<string> Comments,
    string? MappingError);
