using Ghos.Web.Data;

namespace Ghos.Web.ProjectTools;

public static class QuotePricing
{
    public static decimal GetUnitPrice(
        decimal retailPrice,
        decimal? contractorTier1Price,
        decimal? contractorTier2Price,
        QuoteAudience audience,
        ContractorTier contractorTier)
    {
        return audience switch
        {
            QuoteAudience.Contractor when contractorTier == ContractorTier.Tier2 =>
                contractorTier2Price ?? contractorTier1Price ?? retailPrice,
            QuoteAudience.Contractor =>
                contractorTier1Price ?? contractorTier2Price ?? retailPrice,
            _ => retailPrice
        };
    }

    public static string GetPricingLabel(
        QuoteAudience audience,
        ContractorTier contractorTier) =>
        audience switch
        {
            QuoteAudience.Contractor when contractorTier == ContractorTier.Tier2 =>
                "Contractor Tier 2",
            QuoteAudience.Contractor => "Contractor Tier 1",
            QuoteAudience.Custom => "Custom",
            _ => "Customer"
        };
}
