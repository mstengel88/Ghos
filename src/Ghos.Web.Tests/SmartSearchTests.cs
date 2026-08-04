using System.ComponentModel.DataAnnotations;
using Ghos.Web.Data;
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
        Assert.Contains("silver", plan.ExpandedTerms);
        Assert.DoesNotContain("charcoal", plan.ExpandedTerms);
    }

    [Fact]
    public void Plan_DoesNotInventIntentForUnknownLanguage()
    {
        var plan = SmartSearchSynonymLibrary.Plan("something unusual");

        Assert.Empty(plan.Intents);
        Assert.Contains("something", plan.DirectTerms);
        Assert.Contains("unusual", plan.DirectTerms);
    }

    [Fact]
    public void IntentCoverage_RequiresTheProductToSupportEachMeaning()
    {
        var plan = SmartSearchSynonymLibrary.Plan(
            "gray stone for a patio");

        var strongMatches =
            SmartProductSearchService.EvaluateIntentMatches(
                "gray crushed limestone stone for patios and outdoor seating",
                plan);
        var partialMatches =
            SmartProductSearchService.EvaluateIntentMatches(
                "gray decorative stone for landscape beds",
                plan);

        Assert.Contains("Use: Patios", strongMatches);
        Assert.Contains("Material: Crushed stone", strongMatches);
        Assert.Contains("Color: Gray", strongMatches);
        Assert.DoesNotContain("Use: Patios", partialMatches);
        Assert.Contains("Material: Crushed stone", partialMatches);
        Assert.Contains("Color: Gray", partialMatches);
    }

    [Fact]
    public void IntentCoverage_DoesNotMatchTermsInsideDifferentWords()
    {
        var plan = SmartSearchSynonymLibrary.Plan("crushed stone");

        var matches = SmartProductSearchService.EvaluateIntentMatches(
            "paver leveling sand for stepping stones",
            plan);

        Assert.DoesNotContain("Material: Crushed stone", matches);
    }

    [Fact]
    public void Plan_AppliesAnActiveCustomCustomerPhrase()
    {
        var plan = SmartSearchSynonymLibrary.Plan(
            "I need crusher dust",
            [new SmartSearchCustomSynonym(
                "crusher dust",
                "stone screenings")]);

        Assert.Contains("stone screenings", plan.ExpandedTerms);
        Assert.Contains(
            "Custom: crusher dust → stone screenings",
            plan.Intents);
    }

    [Fact]
    public void Plan_DoesNotApplyCustomPhraseInsideAnotherWord()
    {
        var plan = SmartSearchSynonymLibrary.Plan(
            "dustpan",
            [new SmartSearchCustomSynonym(
                "dust",
                "stone screenings")]);

        Assert.DoesNotContain("stone screenings", plan.ExpandedTerms);
        Assert.DoesNotContain(
            plan.Intents,
            intent => intent.StartsWith("Custom:"));
    }

    [Theory]
    [InlineData("  Crusher   Dust  ", "crusher dust")]
    [InlineData("3/8 STONE", "3/8 stone")]
    public void Tuning_NormalizesCustomerRules(
        string value,
        string expected)
    {
        Assert.Equal(
            expected,
            SmartSearchTuningService.NormalizeRequired(
                value,
                "Customer phrase"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("!")]
    [InlineData("a")]
    public void Tuning_RejectsUnusableCustomerRules(string value)
    {
        Assert.Throws<ValidationException>(() =>
            SmartSearchTuningService.NormalizeRequired(
                value,
                "Customer phrase"));
    }

    [Fact]
    public void ReviewRecommendation_DistinguishesCatalogDemand()
    {
        var recommendation =
            SmartProductSearchService.BuildReviewRecommendation(
                0,
                null,
                null);

        Assert.Contains("No products matched", recommendation);
        Assert.Contains("customer demand", recommendation);
    }

    [Fact]
    public void ReviewRecommendation_UsesTheActualProductAndMissingFacts()
    {
        var recommendation =
            SmartProductSearchService.BuildReviewRecommendation(
                6,
                "Alpine Stone",
                "Color: Gray · Use: Patios");

        Assert.Contains("Alpine Stone", recommendation);
        Assert.Contains("Gray", recommendation);
        Assert.Contains("Patios", recommendation);
        Assert.Contains("Shopify tags", recommendation);
        Assert.Contains("GHOS Best Uses", recommendation);
    }

    [Theory]
    [InlineData("Pin", SmartSearchMerchandisingRuleTypes.Pin)]
    [InlineData(" Boost ", SmartSearchMerchandisingRuleTypes.Boost)]
    public void Merchandising_ValidatesRuleTypes(
        string value,
        string expected)
    {
        Assert.Equal(
            expected,
            SmartSearchMerchandisingService.NormalizeRuleType(value));
    }

    [Fact]
    public void Merchandising_PinDoesNotChangeOrganicConfidence()
    {
        var result = SearchResult(
            confidence: "Medium",
            unmatchedIntents: ["Use: Patios"]);
        var rule = new SmartSearchMerchandisingRule
        {
            RuleType = SmartSearchMerchandisingRuleTypes.Pin,
            PinPosition = 1
        };

        var ranked =
            SmartProductSearchService.ApplyMerchandising(
                result,
                rule);

        Assert.Equal(1, ranked.PinnedPosition);
        Assert.Equal("Pinned for this search", ranked.MerchandisingLabel);
        Assert.Equal("Medium", ranked.Confidence);
        Assert.Contains("Use: Patios", ranked.UnmatchedIntents);
        Assert.Equal(result.Score, ranked.Score);
    }

    [Fact]
    public void Merchandising_BoostPreservesOrganicScore()
    {
        var result = SearchResult();
        var rule = new SmartSearchMerchandisingRule
        {
            RuleType = SmartSearchMerchandisingRuleTypes.Boost,
            BoostPoints = 75
        };

        var ranked =
            SmartProductSearchService.ApplyMerchandising(
                result,
                rule);

        Assert.Equal(75, ranked.MerchandisingBoost);
        Assert.Equal("Ranking boosted", ranked.MerchandisingLabel);
        Assert.Equal(result.Score, ranked.Score);
        Assert.Null(ranked.PinnedPosition);
    }

    [Theory]
    [InlineData("drivway ston", "driveway stone")]
    [InlineData("mulhc for gardne", "mulch for garden")]
    public void TypoCorrection_RepairsObviousCatalogMisspellings(
        string query,
        string expected)
    {
        var correction = SmartSearchTypoCorrector.Suggest(
            query,
            ["driveway", "stone", "mulch", "garden"]);

        Assert.NotNull(correction);
        Assert.Equal(expected, correction.CorrectedQuery);
        Assert.NotEmpty(correction.Replacements);
    }

    [Fact]
    public void TypoCorrection_DoesNotRewriteAmbiguousWords()
    {
        var correction = SmartSearchTypoCorrector.Suggest(
            "stane",
            ["stone", "stage"]);

        Assert.Null(correction);
    }

    [Fact]
    public void TypoCorrection_DoesNotRewriteUnknownCustomerLanguage()
    {
        var correction = SmartSearchTypoCorrector.Suggest(
            "pizza oven",
            ["stone", "gravel", "mulch", "topsoil"]);

        Assert.Null(correction);
    }

    [Fact]
    public void TypoCorrection_PreservesCorrectCatalogTerms()
    {
        var correction = SmartSearchTypoCorrector.Suggest(
            "gray driveway stone",
            ["gray", "driveway", "stone"]);

        Assert.Null(correction);
    }

    [Fact]
    public void TypoCorrection_PreservesNormalCustomerWording()
    {
        var correction = SmartSearchTypoCorrector.Suggest(
            "need some stone",
            ["reed", "stone"]);

        Assert.Null(correction);
    }

    [Fact]
    public void Suggestions_PrioritizeActiveCatalogProducts()
    {
        var suggestions = SmartSearchSuggestionRanker.Rank(
            "drive",
            [
                new(
                    "Driveway materials",
                    "Search"),
                new(
                    "Driveway Stone #2",
                    "Product",
                    "Bulk Stone",
                    "https://greenhillssupply.com/products/2-stone")
            ]);

        Assert.Equal("Driveway Stone #2", suggestions[0].Text);
        Assert.Equal("Product", suggestions[0].Kind);
        Assert.Equal(
            "https://greenhillssupply.com/products/2-stone",
            suggestions[0].ProductUrl);
    }

    [Fact]
    public void Suggestions_UseWordPrefixesWithoutFuzzyGuessing()
    {
        var suggestions = SmartSearchSuggestionRanker.Rank(
            "grav",
            [
                new("Drainage Gravel", "Search"),
                new("Garden Stone", "Search")
            ]);

        Assert.Single(suggestions);
        Assert.Equal("Drainage Gravel", suggestions[0].Text);
    }

    [Fact]
    public void Suggestions_DeduplicateEquivalentCatalogLanguage()
    {
        var suggestions = SmartSearchSuggestionRanker.Rank(
            "mul",
            [
                new("Mulch", "Search"),
                new("MULCH", "Popular", Popularity: 12)
            ]);

        Assert.Single(suggestions);
        Assert.Equal("Popular", suggestions[0].Kind);
    }

    [Fact]
    public void Suggestions_RequireAtLeastTwoCharacters()
    {
        var suggestions = SmartSearchSuggestionRanker.Rank(
            "s",
            [new("Stone", "Product")]);

        Assert.Empty(suggestions);
    }

    private static SmartProductSearchResult SearchResult(
        string confidence = "High",
        IReadOnlyList<string>? unmatchedIntents = null) =>
        new(
            Guid.NewGuid(),
            "Test Stone",
            "https://greenhillssupply.com/products/test-stone",
            null,
            null,
            10m,
            100,
            confidence,
            [],
            unmatchedIntents ?? [],
            [],
            null,
            0,
            null);
}
