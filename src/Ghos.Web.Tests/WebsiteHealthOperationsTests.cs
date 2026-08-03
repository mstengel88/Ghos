using System.ComponentModel.DataAnnotations;
using Ghos.Web.WebsiteHealth;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class WebsiteHealthOperationsTests
{
    [Theory]
    [InlineData(" /collections/all ", "/collections/all")]
    [InlineData("/products/stone?variant=1", "/products/stone?variant=1")]
    public void NormalizeKeyPagePath_AcceptsRelativeSitePaths(
        string input,
        string expected)
    {
        var result =
            WebsiteHealthSettingsService.NormalizeKeyPagePath(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("products/stone")]
    [InlineData("//example.com/path")]
    [InlineData("https://example.com/path")]
    public void NormalizeKeyPagePath_RejectsUnsafeOrInvalidPaths(
        string input)
    {
        Assert.Throws<ValidationException>(
            () => WebsiteHealthSettingsService.NormalizeKeyPagePath(input));
    }

    [Fact]
    public void NormalizeNote_TrimsAndConvertsBlankTextToNull()
    {
        Assert.Equal(
            "Investigating with the content team.",
            WebsiteHealthIssueService.NormalizeNote(
                "  Investigating with the content team.  "));
        Assert.Null(WebsiteHealthIssueService.NormalizeNote("   "));
    }

    [Fact]
    public void NormalizeNote_LimitsPersistedNotesToModelLength()
    {
        var result =
            WebsiteHealthIssueService.NormalizeNote(new string('a', 1001));

        Assert.NotNull(result);
        Assert.Equal(1000, result.Length);
    }

    [Fact]
    public void MissingMetaDescription_CreatesHomepageCopyWithinSearchLength()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                new Uri("https://www.greenhillssupply.com/"),
                "Green Hills Supply",
                "Landscape and outdoor materials",
                null);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.InRange(recommendation.SuggestedValue.Length, 120, 155);
        Assert.Contains(
            "Green Hills Supply",
            recommendation.SuggestedValue);
        Assert.Contains(
            "pickup and delivery",
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingMetaDescription_UsesNoIndexForUtilityPages()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                new Uri("https://www.greenhillssupply.com/cart"),
                "Your cart",
                null,
                null);

        Assert.Equal(
            """<meta name="robots" content="noindex,follow">""",
            recommendation.SuggestedValue);
    }

    [Fact]
    public void MissingMetaDescription_DistinguishesPaginatedCollections()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                new Uri(
                    "https://www.greenhillssupply.com/collections/all?page=3"),
                "All products",
                "All products",
                null);

        Assert.Contains("page 3", recommendation.SuggestedValue);
    }

    [Fact]
    public void MissingTitle_ProducesBrandedTitleWithinRecommendedLength()
    {
        var recommendation = WebsiteHealthRecommendationBuilder.MissingTitle(
            new Uri(
                "https://www.greenhillssupply.com/collections/decorative-stone"),
            "Decorative Stone");

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.EndsWith(
            "Green Hills Supply",
            recommendation.SuggestedValue);
        Assert.InRange(recommendation.SuggestedValue.Length, 1, 60);
    }

    [Fact]
    public void MissingImageAltText_UsesVisibleImageContext()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingImageAltText(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                [
                    new WebsiteHealthMissingImage(
                        "https://cdn.example.com/red-mulch.jpg",
                        "Premium red mulch")
                ]);

        Assert.Equal(
            "red-mulch.jpg: alt=\"Premium red mulch\"",
            recommendation.SuggestedValue);
    }

    [Fact]
    public void MissingImageAltText_RecognizesCurrentStorefrontLogo()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingImageAltText(
                new Uri("https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                [
                    new WebsiteHealthMissingImage(
                        "//cdn.shopify.com/files/Untitled_design_2.png?v=1",
                        null),
                    new WebsiteHealthMissingImage(
                        "//cdn.shopify.com/files/Untitled_design_2.png?v=1",
                        null)
                ]);

        Assert.Contains(
            "alt=\"Green Hills Supply logo\"",
            recommendation.SuggestedValue);
        Assert.Contains(
            "1 repeated image occurrence",
            recommendation.SuggestedValue);
    }

    [Fact]
    public void MissingCanonical_RemovesQueryParameters()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingCanonical(
                new Uri(
                    "https://www.greenhillssupply.com/collections/all?page=2"));

        Assert.Equal(
            """<link rel="canonical" href="https://www.greenhillssupply.com/collections/all">""",
            recommendation.SuggestedValue);
    }
}
