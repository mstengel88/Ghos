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
    public void MissingImageAltText_DoesNotGuessFromGenericFilename()
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
            "alt=\"Mulch\"",
            recommendation.SuggestedValue);
        Assert.Contains(
            "1 repeated image occurrence",
            recommendation.SuggestedValue);
    }

    [Theory]
    [InlineData(false, null, false, false, true)]
    [InlineData(true, "Red mulch product bag", false, false, false)]
    [InlineData(true, "", false, false, false)]
    [InlineData(true, "", true, true, false)]
    [InlineData(true, "", true, false, true)]
    public void IsMissingAlternativeText_UsesAccessibleImageSemantics(
        bool hasAltAttribute,
        string? altText,
        bool isInsideInteractiveControl,
        bool interactiveControlHasAccessibleName,
        bool expected)
    {
        var result = WebsiteHealthMonitorService.IsMissingAlternativeText(
            hasAltAttribute,
            altText,
            isInsideInteractiveControl,
            interactiveControlHasAccessibleName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeImageAssetUrl_GroupsResponsiveVariants()
    {
        var normalized = WebsiteHealthMonitorService.NormalizeImageAssetUrl(
            new Uri("https://www.greenhillssupply.com/collections/mulch"),
            "//greenhillssupply.com/cdn/shop/files/icon.png?v=1&width=30");

        Assert.Equal(
            "https://greenhillssupply.com/cdn/shop/files/icon.png",
            normalized);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/",
        "Online Store → Preferences")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/all",
        "create a separate smart collection")]
    [InlineData(
        "https://www.greenhillssupply.com/blogs/news",
        "Manage blogs")]
    [InlineData(
        "https://www.greenhillssupply.com/blogs/news/how-to-use-mulch",
        "open this post")]
    public void GetShopifySeoLocation_IdentifiesExactAdminSurface(
        string url,
        string expectedText)
    {
        var location =
            WebsiteHealthRecommendationBuilder.GetShopifySeoLocation(
                new Uri(url));

        Assert.Contains(expectedText, location);
    }

    [Fact]
    public void MissingMetaDescription_DoesNotRecommendRenamingACollectionForCatalog()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                new Uri(
                    "https://www.greenhillssupply.com/collections/all"),
                "Products",
                "Products",
                null);

        Assert.Contains(
            "Do not rename",
            recommendation.Guidance);
        Assert.Contains(
            "separate smart collection",
            recommendation.FixLocation);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/",
        "/promoting-marketing/seo/adding-keywords")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/all",
        "/change-catalog-page")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "/collections/make-collections-available")]
    [InlineData(
        "https://www.greenhillssupply.com/blogs/news",
        "/blogs/adding-a-blog")]
    [InlineData(
        "https://www.greenhillssupply.com/blogs/news/mulch-guide",
        "/working-with-blog-posts")]
    public void GetShopifySeoDocumentation_UsesOfficialResourceDocumentation(
        string url,
        string expectedPath)
    {
        var documentation =
            WebsiteHealthRecommendationBuilder.GetShopifySeoDocumentation(
                new Uri(url));

        Assert.NotNull(documentation);
        Assert.StartsWith("https://help.shopify.com/", documentation);
        Assert.Contains(expectedPath, documentation);
    }

    [Fact]
    public void GetShopifySeoDocumentation_LeavesThemeDependentRoutesUnverified()
    {
        var documentation =
            WebsiteHealthRecommendationBuilder.GetShopifySeoDocumentation(
                new Uri("https://www.greenhillssupply.com/search"));

        Assert.Null(documentation);
    }

    [Fact]
    public void MetaDescriptionLength_CondensesTheCurrentLiveCollectionCopy()
    {
        const string current =
            "Our aggregate materials provide the strong, stable foundation your project demands. From crushed limestone and gravel to base and drainage stone, our aggregates are ideal for driveways, concrete prep, utility work, compaction, and structural fill. Available in bulk for pickup or delivery, we supply consistent, high-quality materials.";
        var recommendation =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/collections/aggregate"),
                "Aggregate",
                "Aggregate",
                null,
                current);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.Contains(
            "crushed limestone",
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.InRange(recommendation.SuggestedValue.Length, 120, 155);
        Assert.EndsWith(".", recommendation.SuggestedValue);
        Assert.Contains(
            "condensed from the current live description",
            recommendation.Guidance);
    }

    [Fact]
    public void MetaDescriptionLength_ProducesDistinctCollectionSpecificCopy()
    {
        var aggregate =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/collections/aggregate"),
                "Aggregate",
                "Aggregate",
                null,
                "Build reliable driveways and project bases with crushed limestone, gravel, drainage stone, and compactable aggregate available for pickup or delivery throughout the region.");
        var mulch =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                null,
                "Finish planting beds with natural hardwood mulch, rich dark mulch, and long-lasting decorative options that retain moisture and give landscapes a polished appearance.");

        Assert.Contains(
            "crushed limestone",
            aggregate.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "hardwood mulch",
            mulch.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            aggregate.SuggestedValue,
            mulch.SuggestedValue);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/collections/all?page=2",
        "https://www.greenhillssupply.com/collections/all")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch?page=3",
        "https://www.greenhillssupply.com/collections/mulch")]
    [InlineData(
        "https://www.greenhillssupply.com/search?q=stone",
        "https://www.greenhillssupply.com/search?q=stone")]
    public void NormalizeMetadataTarget_GroupsOnlyCollectionPagination(
        string input,
        string expected)
    {
        var normalized = WebsiteHealthMonitorService.NormalizeMetadataTarget(
            new Uri(input));

        Assert.Equal(expected, normalized);
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

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void IsMetadataLengthHealthy_UsesInclusiveBoundaries(
        int length,
        bool expected)
    {
        var result = WebsiteHealthMonitorService.IsMetadataLengthHealthy(
            new string('a', length),
            20,
            60);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeComparableMetadata_DetectsWhitespaceAndCaseDuplicates()
    {
        var normalized =
            WebsiteHealthMonitorService.NormalizeComparableMetadata(
                "  Shop  Stone &amp;\nMulch ");

        Assert.Equal("shop stone & mulch", normalized);
    }

    [Fact]
    public void DuplicateMetaDescription_ProducesUniqueReplacementAndLocation()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.DuplicateMetaDescription(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                null,
                new Uri(
                    "https://www.greenhillssupply.com/collections/stone"));

        Assert.Contains(
            "/collections/stone",
            recommendation.Guidance);
        Assert.Contains(
            "Shopify Admin → Products → Collections",
            recommendation.FixLocation);
        Assert.NotNull(recommendation.SuggestedValue);
        Assert.InRange(recommendation.SuggestedValue.Length, 70, 155);
    }
}
