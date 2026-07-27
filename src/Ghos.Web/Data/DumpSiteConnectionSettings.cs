using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class DumpSiteConnectionSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(500)]
    public string BridgeApiBaseUrl { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string EncryptedSharedSecret { get; set; } = string.Empty;

    [MaxLength(100)]
    public string BridgeId { get; set; } = "ghos-dump-site-operator";

    public string ItemMappingsJson { get; set; } = "{}";

    public string CompanyMappingsJson { get; set; } = "{}";

    [MaxLength(20)]
    public string CounterpointLocation { get; set; } = "101";

    [MaxLength(20)]
    public string CounterpointStation { get; set; } = "201-01";

    [MaxLength(20)]
    public string CounterpointDrawer { get; set; } = "201-01";

    [MaxLength(30)]
    public string CounterpointSalesRep { get; set; } = "EC_SHOPIFY";

    public DateTime? LastHealthCheckAtUtc { get; set; }

    public bool? LastHealthCheckSucceeded { get; set; }

    [MaxLength(500)]
    public string? LastHealthCheckMessage { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? UpdatedByUserId { get; set; }
}
