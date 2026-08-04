using Ghos.Web.SmartSearch;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class SmartSearchTests
{
    [Fact]
    public void SynonymLibrary_ContainsMoreThanFiveHundredMappings()
    {
        Assert.True(
            SmartSearchSynonymLibrary.SynonymMappingCount >= 500,
            $"Expected at least 500 mappings, found " +
            $"{SmartSearchSynonymLibrary.SynonymMappingCount}.");
    }

    [Fact]
    public void Plan_ExpandsCustomerUseLanguage()
    {
        var plan = SmartSearchSynonymLibrary.Plan(
            "stone for my driveway");

        Assert.Contains("Use: Driveways", plan.Intents);
        Assert.Contains("Material: Crushed stone", plan.Intents);
        Assert.Contains("parking pad", plan.ExpandedTerms);
        Assert.Contains("gravel drive", plan.ExpandedTerms);
        Assert.DoesNotContain("for", plan.DirectTerms);
        Assert.DoesNotContain("my", plan.DirectTerms);
    }

    [Fact]
    public void Plan_RecognizesColorSizeAndProjectTogether()
    {
        var plan = SmartSearchSynonymLibrary.Plan(
            "small gray stone for a patio");

        Assert.Contains("Color: Gray", plan.Intents);
        Assert.Contains("Size: Small", plan.Intents);
        Assert.Contains("Use: Patios", plan.Intents);
        Assert.Contains("pea gravel", plan.ExpandedTerms);
        Assert.Contains("charcoal", plan.ExpandedTerms);
    }

    [Fact]
    public void Plan_DoesNotInventIntentForUnknownLanguage()
    {
        var plan = SmartSearchSynonymLibrary.Plan("something unusual");

        Assert.Empty(plan.Intents);
        Assert.Contains("something", plan.DirectTerms);
        Assert.Contains("unusual", plan.DirectTerms);
    }
}
