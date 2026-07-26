using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class BulkOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(40)]
    public string TargetType { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Summary { get; set; } = string.Empty;

    public int RecordCount { get; set; }

    public DateTime PerformedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? PerformedByUserId { get; set; }

    [MaxLength(160)]
    public string PerformedByName { get; set; } = string.Empty;
}

public static class BulkOperationTargets
{
    public const string Products = "Products";

    public const string DigitalAssets = "DigitalAssets";
}
