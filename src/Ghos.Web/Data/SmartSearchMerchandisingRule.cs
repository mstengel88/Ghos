using System.ComponentModel.DataAnnotations;

namespace Ghos.Web.Data;

public sealed class SmartSearchMerchandisingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public string QueryPhrase { get; set; } = string.Empty;

    [MaxLength(120)]
    public string NormalizedQueryPhrase { get; set; } = string.Empty;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [MaxLength(16)]
    public string RuleType { get; set; } =
        SmartSearchMerchandisingRuleTypes.Boost;

    public int BoostPoints { get; set; } = 75;

    public int PinPosition { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string CreatedByUserId { get; set; } = string.Empty;
}

public static class SmartSearchMerchandisingRuleTypes
{
    public const string Pin = "Pin";
    public const string Boost = "Boost";
}
