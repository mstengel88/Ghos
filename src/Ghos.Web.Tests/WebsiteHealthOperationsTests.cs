using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Ghos.Web.WebsiteHealth;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class WebsiteHealthOperationsTests
{
    [Theory]
    [InlineData(
        "2026-08-04T13:55:00Z",
        "Aug 4, 2026 8:55 AM CDT")]
    [InlineData(
        "2026-01-15T18:00:00Z",
        "Jan 15, 2026 12:00 PM CST")]
    public void WebsiteHealthTimeFormatter_UsesChicagoCentralTime(
        string utcValue,
        string expected)
    {
        var formatted =
            WebsiteHealthTimeFormatter.FormatTimestamp(
                DateTime.Parse(
                    utcValue,
                    null,
                    System.Globalization.DateTimeStyles
                        .AdjustToUniversal));

        Assert.Equal(expected, formatted);
    }

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
    public void NormalizeReviewedValue_TrimsRejectsBlankAndLimitsLength()
    {
        Assert.Equal(
            "Tailored reviewed description.",
            WebsiteHealthIssueService.NormalizeReviewedValue(
                "  Tailored reviewed description.  "));
        Assert.Null(
            WebsiteHealthIssueService.NormalizeReviewedValue("   "));

        var result = WebsiteHealthIssueService.NormalizeReviewedValue(
            new string('a', 6001));
        Assert.NotNull(result);
        Assert.Equal(6000, result.Length);
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
    public void MissingMetaDescription_UsesOneCatalogDescriptionForPagination()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                new Uri(
                    "https://www.greenhillssupply.com/collections/all?page=3"),
                "All products",
                "All products",
                null);

        Assert.Equal(
            "Shop Green Hills Supply for aggregate, decorative stone, mulch, sand, bagged landscape materials, tools, ice melt, and water-conditioning products.",
            recommendation.SuggestedValue);
        Assert.InRange(
            recommendation.SuggestedValue!.Length,
            120,
            155);
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
    [InlineData("image", "/files/red-mulch.jpg", true)]
    [InlineData("Untitled Design 2", "/files/photo.png", true)]
    [InlineData("IMG_2048", "/files/photo.jpg", true)]
    [InlineData("red-mulch", "/files/red-mulch.jpg", true)]
    [InlineData(
        "Premium red mulch installed in a garden bed",
        "/files/red-mulch.jpg",
        false)]
    [InlineData(
        "Green Hills Supply logo",
        "/files/green-hills-logo.png",
        false)]
    public void ImageAltQuality_DetectsOnlyGenericOrFilenameText(
        string altText,
        string source,
        bool expected)
    {
        Assert.Equal(
            expected,
            WebsiteHealthMonitorService.IsGenericImageAlt(
                altText,
                source));
    }

    [Fact]
    public void ImageAltQuality_TailorsEachDifferentImageSuggestion()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.ImageAltQuality(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                "Product image",
                [
                    new WebsiteHealthImage(
                        "https://cdn.example.com/red-mulch.jpg",
                        "Product image",
                        "Premium red mulch"),
                    new WebsiteHealthImage(
                        "https://cdn.example.com/brown-mulch.jpg",
                        "Product image",
                        "Natural brown mulch")
                ],
                true);

        Assert.Contains(
            "red-mulch.jpg: alt=\"Premium red mulch\"",
            recommendation.SuggestedValue);
        Assert.Contains(
            "brown-mulch.jpg: alt=\"Natural brown mulch\"",
            recommendation.SuggestedValue);
        Assert.Contains(
            "Current alt text: \"Product image\"",
            recommendation.CurrentValue);
        Assert.Contains(
            "if this is a product-card image",
            recommendation.FixLocation);

        var evidence = JsonSerializer.Deserialize<
            List<WebsiteHealthImageEvidence>>(
                recommendation.EvidenceJson!);
        Assert.NotNull(evidence);
        Assert.Collection(
            evidence,
            item =>
            {
                Assert.Equal(
                    "https://cdn.example.com/red-mulch.jpg",
                    item.SourceUrl);
                Assert.Equal("Product image", item.CurrentAltText);
                Assert.Equal("Premium red mulch", item.SuggestedAltText);
                Assert.Equal("Nearby page context", item.SuggestionSource);
            },
            item =>
            {
                Assert.Equal(
                    "https://cdn.example.com/brown-mulch.jpg",
                    item.SourceUrl);
                Assert.Equal("Natural brown mulch", item.SuggestedAltText);
            });
    }

    [Fact]
    public void ImageAltQuality_DoesNotRecommendStockLibraryFilenames()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.ImageAltQuality(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch",
                "Mulch",
                "Green Hills Supply",
                [
                    new WebsiteHealthImage(
                        "https://cdn.example.com/iStock-1402995921.jpg",
                        "Green Hills Supply",
                        null)
                ],
                true);

        Assert.Contains(
            "alt=\"Mulch\"",
            recommendation.SuggestedValue);
        Assert.DoesNotContain(
            "alt=\"Istock\"",
            recommendation.SuggestedValue);
    }

    [Fact]
    public void ImageAltQuality_PointsHomepageImagesToRealShopifySections()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.ImageAltQuality(
                new Uri("https://www.greenhillssupply.com/"),
                "Green Hills Supply",
                "Green Hills Supply",
                "Green Hills Supply",
                [
                    new WebsiteHealthImage(
                        "https://cdn.example.com/hero.jpg",
                        "Green Hills Supply",
                        "Decorative Stone")
                ],
                true);

        Assert.Contains("Home page", recommendation.FixLocation);
        Assert.Contains("Slideshow", recommendation.FixLocation);
        Assert.Contains("Collection tabs", recommendation.FixLocation);
        Assert.Contains("Content → Files", recommendation.FixLocation);
    }

    [Fact]
    public async Task ImageContext_UsesLinkedCollectionBeforeNavigationText()
    {
        var document = await new HtmlParser().ParseDocumentAsync(
            """
            <header>
              <a href="/">
                <img class="logo" src="/logo.png" alt="Green Hills Supply">
                Search Clear Pro-Access Hub Sign in Register Cart
              </a>
            </header>
            <main>
              <a href="/collections/mulch" aria-label="Mulch">
                <picture><img src="/mulch.jpg" alt="Green Hills Supply"></picture>
              </a>
            </main>
            """);
        var image = document.QuerySelector("main img");

        Assert.NotNull(image);
        Assert.Equal(
            "Mulch",
            WebsiteHealthMonitorService.GetImageContext(
                image,
                new Uri("https://www.greenhillssupply.com/")));
    }

    [Fact]
    public async Task ImageContext_UsesHeadingFromItsOwnSlideshowPanel()
    {
        var document = await new HtmlParser().ParseDocumentAsync(
            """
            <div class="slideshow__item">
              <div><div><motion-element><picture>
                <img src="/aggregate.jpg" alt="Green Hills Supply">
              </picture></motion-element></div></div>
              <div><h2>Aggregate</h2></div>
            </div>
            <div class="slideshow__item">
              <div><div><motion-element><picture>
                <img src="/mulch.jpg" alt="Green Hills Supply">
              </picture></motion-element></div></div>
              <div><h2>Mulch</h2></div>
            </div>
            """);
        var images = document.QuerySelectorAll("img");

        Assert.Equal(
            "Aggregate",
            WebsiteHealthMonitorService.GetImageContext(
                images[0],
                new Uri("https://www.greenhillssupply.com/")));
        Assert.Equal(
            "Mulch",
            WebsiteHealthMonitorService.GetImageContext(
                images[1],
                new Uri("https://www.greenhillssupply.com/")));
    }

    [Fact]
    public void ImageAssetKey_TreatsShopifyFileAndCollectionCopiesAsSameAsset()
    {
        var page = new Uri("https://www.greenhillssupply.com/");

        Assert.Equal(
            WebsiteHealthMonitorService.NormalizeImageAssetKey(
                page,
                "/cdn/shop/files/iStock-1402995921.jpg?v=1"),
            WebsiteHealthMonitorService.NormalizeImageAssetKey(
                page,
                "/cdn/shop/collections/iStock-1402995921.jpg?v=2"));
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
        "search for All")]
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
            "exists even when no All collection appears",
            recommendation.Guidance);
        Assert.Contains(
            "does not replace your separate collections landing page",
            recommendation.Guidance);
        Assert.Contains(
            "new Collections experience",
            recommendation.Guidance);
        Assert.Contains(
            "If no editable All collection exists",
            recommendation.FixLocation,
            StringComparison.Ordinal);
        Assert.Contains(
            "default Products source",
            recommendation.FixLocation);
        Assert.Contains(
            "Add condition (not Add products)",
            recommendation.FixLocation);
        Assert.Contains(
            "no Smart or Manual type selector",
            recommendation.FixLocation);
        Assert.Contains(
            "Leave your existing collections landing page",
            recommendation.FixLocation);
        Assert.DoesNotContain(
            "change any menu link",
            recommendation.FixLocation,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/",
        "/promoting-marketing/seo/adding-keywords")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/all",
        "/collections/create-collection")]
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
    public void StructuredDataAnalysis_ParsesNestedTypesAndCountsMalformedBlocks()
    {
        var analysis = WebsiteHealthMonitorService.AnalyzeStructuredData(
        [
            """
            {
              "@context": "https://schema.org",
              "@type": "Product",
              "offers": {
                "@type": ["Offer", "AggregateOffer"]
              }
            }
            """,
            """{"@context":"https://schema.org","@type":"""
        ]);

        Assert.Equal(2, analysis.BlockCount);
        Assert.Equal(1, analysis.ValidBlockCount);
        Assert.Equal(1, analysis.InvalidBlockCount);
        Assert.Contains("Product", analysis.SchemaTypes);
        Assert.Contains("Offer", analysis.SchemaTypes);
        Assert.Contains("AggregateOffer", analysis.SchemaTypes);
    }

    [Fact]
    public void ProductStructuredDataAnalysis_AcceptsLiveShopifyOfferFields()
    {
        var analysis = WebsiteHealthMonitorService.AnalyzeStructuredData(
        [
            """
            {
              "@context": "https://schema.org",
              "@type": "Product",
              "name": "#1 Stone",
              "image": "https://cdn.example.com/1-stone.jpg",
              "url": "https://greenhillssupply.com/products/1-stone",
              "offers": {
                "@type": "Offer",
                "price": "28.99",
                "priceCurrency": "USD",
                "availability": "https://schema.org/InStock"
              }
            }
            """
        ]);

        var problems =
            WebsiteHealthMonitorService.GetProductSchemaProblems(
                new Uri(
                    "https://www.greenhillssupply.com/products/1-stone"),
                analysis.SchemaTypes,
                analysis.Product);

        Assert.Empty(problems);
    }

    [Fact]
    public void ProductStructuredDataAnalysis_ReportsMissingMerchantFields()
    {
        var analysis = WebsiteHealthMonitorService.AnalyzeStructuredData(
        [
            """
            {
              "@context": "https://schema.org",
              "@type": "Product",
              "name": "Clear Stone",
              "offers": { "@type": "Offer" }
            }
            """
        ]);

        var problems =
            WebsiteHealthMonitorService.GetProductSchemaProblems(
                new Uri(
                    "https://www.greenhillssupply.com/products/clear-stone"),
                analysis.SchemaTypes,
                analysis.Product);

        Assert.Contains("Product image is missing", problems);
        Assert.Contains("Offer price is missing", problems);
        Assert.Contains("Offer priceCurrency is missing", problems);
        Assert.Contains("Offer availability is missing", problems);
        Assert.Contains(
            "Product URL does not match this page",
            problems);
    }

    [Fact]
    public void ProductStructuredDataAnalysis_AcceptsProductGroupCanonicalUrl()
    {
        var analysis = WebsiteHealthMonitorService.AnalyzeStructuredData(
        [
            """
            {
              "@context": "https://schema.org",
              "@type": "ProductGroup",
              "name": "Alpine Stone",
              "url": "https://greenhillssupply.com/products/alpine-stone",
              "hasVariant": [{
                "@type": "Product",
                "name": "Alpine Stone - Medium",
                "image": "https://cdn.example.com/alpine-medium.jpg",
                "offers": {
                  "@type": "Offer",
                  "price": "29.99",
                  "priceCurrency": "USD",
                  "availability": "https://schema.org/InStock"
                }
              }]
            }
            """
        ]);

        var problems =
            WebsiteHealthMonitorService.GetProductSchemaProblems(
                new Uri(
                    "https://www.greenhillssupply.com/products/alpine-stone"),
                analysis.SchemaTypes,
                analysis.Product);

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/",
        "Organization",
        "WebSite")]
    [InlineData(
        "https://www.greenhillssupply.com/products/black-mulch",
        "Offer",
        "Product")]
    [InlineData(
        "https://www.greenhillssupply.com/blogs/news/mulch-guide",
        "WebPage",
        "Article")]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "Organization",
        null)]
    public void StructuredDataAnalysis_RequiresOnlyPageAppropriateSchema(
        string url,
        string presentType,
        string? expectedMissingType)
    {
        var missingType =
            WebsiteHealthMonitorService.GetMissingExpectedSchemaType(
                new Uri(url),
                new HashSet<string>(
                    [presentType],
                    StringComparer.OrdinalIgnoreCase));

        Assert.Equal(expectedMissingType, missingType);
    }

    [Fact]
    public void SchemaQualityGuidance_SearchesThemeWithoutInventingAFile()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.SchemaQuality(
                new Uri(
                    "https://www.greenhillssupply.com/blogs/news/mulch-guide"),
                0,
                "Article");

        Assert.Contains("Expected schema type: Article", recommendation.SuggestedValue);
        Assert.Contains("search the entire theme", recommendation.FixLocation);
        Assert.DoesNotContain(
            "blog.liquid",
            recommendation.FixLocation,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SocialPreview_UsesLiveProductCopyAndSafeThemeSearchGuidance()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.SocialPreview(
                new Uri(
                    "https://www.greenhillssupply.com/products/clear-stone"),
                "Clear Stone | Green Hills Supply",
                "Clear Stone",
                "Clear stone supports drainage projects and durable bases.",
                "Shop clear stone for drainage projects, landscaping, and durable bases.",
                null,
                null,
                null,
                null,
                null);

        Assert.Contains(
            "Clear Stone | Green Hills Supply",
            recommendation.SuggestedValue);
        Assert.Contains(
            "Shop clear stone for drainage projects",
            recommendation.SuggestedValue);
        Assert.Contains(
            "Products → open this product",
            recommendation.FixLocation);
        Assert.Contains(
            "search the entire theme",
            recommendation.FixLocation);
        Assert.DoesNotContain(
            "social-meta-tags.liquid",
            recommendation.FixLocation);
    }

    [Fact]
    public void BrokenImage_ListsAffectedPagesAndProductMediaLocation()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.BrokenImage(
                new Uri(
                    "https://cdn.shopify.com/files/clear-stone.jpg"),
                [
                    new Uri(
                        "https://www.greenhillssupply.com/products/clear-stone"),
                    new Uri(
                        "https://www.greenhillssupply.com/collections/aggregate")
                ],
                404);

        Assert.Contains("returned HTTP 404", recommendation.Guidance);
        Assert.Contains(
            "/products/clear-stone",
            recommendation.CurrentValue);
        Assert.Contains(
            "/collections/aggregate",
            recommendation.CurrentValue);
        Assert.Contains(
            "Products → open the product",
            recommendation.FixLocation);
    }

    [Fact]
    public void RedirectChain_UsesFinalUrlAndShopifyRedirectLocation()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.RedirectChain(
                new Uri(
                    "https://www.greenhillssupply.com/old-stone"),
                new Uri(
                    "https://www.greenhillssupply.com/products/clear-stone"),
                3);

        Assert.Equal(
            "https://www.greenhillssupply.com/products/clear-stone",
            recommendation.SuggestedValue);
        Assert.Equal(
            "https://www.greenhillssupply.com/old-stone",
            recommendation.CurrentValue);
        Assert.Contains(
            "URL redirects",
            recommendation.FixLocation);
    }

    [Fact]
    public void WebsiteHealthIssueExport_IncludesComparisonAndShopifyFields()
    {
        var csv = WebsiteHealthIssueExportBuilder.BuildCsv(
        [
            new WebsiteHealthIssue
            {
                CheckKey = "meta-description-length",
                Severity = WebsiteHealthIssueSeverity.Warning,
                AffectedUrl =
                    "https://www.greenhillssupply.com/collections/mulch",
                CurrentValue = "A long current description.",
                SuggestedValue = "A tailored mulch description.",
                ReviewedValue = "Reviewed mulch wording.",
                ReviewedAtUtc = new DateTime(
                    2026,
                    8,
                    4,
                    2,
                    30,
                    0,
                    DateTimeKind.Utc),
                FixLocation = "Shopify Admin → Products → Collections",
                FixDocumentationUrl =
                    "https://help.shopify.com/collections",
                TriageNote = "Review with marketing."
            }
        ]);

        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("\"Meta description length\"", csv);
        Assert.Contains("\"A long current description.\"", csv);
        Assert.Contains("\"27\"", csv);
        Assert.Contains("\"A tailored mulch description.\"", csv);
        Assert.Contains("\"Reviewed mulch wording.\"", csv);
        Assert.Contains("\"23\"", csv);
        Assert.Contains("\"Shopify Admin → Products → Collections\"", csv);
        Assert.Contains("\"Review with marketing.\"", csv);
    }

    [Fact]
    public void WebsiteHealthIssueExport_NeutralizesSpreadsheetFormulas()
    {
        var csv = WebsiteHealthIssueExportBuilder.BuildCsv(
        [
            new WebsiteHealthIssue
            {
                CheckKey = "meta-description",
                SuggestedValue = "=HYPERLINK(\"https://example.com\")"
            }
        ]);

        Assert.Contains(
            "\"'=HYPERLINK(\"\"https://example.com\"\")\"",
            csv);
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
        Assert.Contains(
            "limestone, gravel, base",
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.InRange(recommendation.SuggestedValue.Length, 120, 155);
        Assert.EndsWith(".", recommendation.SuggestedValue);
        Assert.DoesNotContain(
            "our.",
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "condensed from the current live description",
            recommendation.Guidance);
        Assert.Equal(current, recommendation.CurrentValue);
    }

    [Fact]
    public void TitleLength_PreservesTheCurrentLiveTitleForComparison()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.TitleLength(
                new Uri("https://www.greenhillssupply.com/"),
                "Landscape and outdoor materials",
                "Green Hills Supply");

        Assert.Equal("Green Hills Supply", recommendation.CurrentValue);
        Assert.NotEqual(
            recommendation.CurrentValue,
            recommendation.SuggestedValue);
    }

    [Theory]
    [InlineData(
        "https://www.greenhillssupply.com/products/1-stone",
        "#1 Stone",
        "#1 Stone | Clean Limestone Gravel for Drainage & Landscape Features – Green Hills Supply",
        "Limestone Gravel",
        "Drainage")]
    [InlineData(
        "https://www.greenhillssupply.com/products/black-mulch",
        "Midnight Black Mulch",
        "Colored Hardwood Mulch | Premium Dyed Mulch for Landscape Bed & Garden – Green Hills Supply",
        "Midnight Black Mulch",
        "Beds & Gardens")]
    [InlineData(
        "https://www.greenhillssupply.com/products/3-8-base",
        "3/8\" Base",
        "3/8\" Base | Crushed Limestone Base for Paths & Light-Duty Projects – Green Hills Supply",
        "3/8\" Base",
        "Paths & Light Projects")]
    [InlineData(
        "https://www.greenhillssupply.com/products/american-heritage",
        "American Heritage",
        "American Heritage Stone | Decorative Landscape Rock for Beds & Accents – Green Hills Supply",
        "American Heritage Stone",
        "Landscape Rock")]
    [InlineData(
        "https://www.greenhillssupply.com/products/premium-blend-mulch",
        "Premium Blend Mulch",
        "Premium Blend Mulch | Triple-Ground Mulch for Landscape Beds & Gardens – Green Hills Supply",
        "Premium Blend Mulch",
        "Triple-Ground for Beds & Gardens")]
    [InlineData(
        "https://www.greenhillssupply.com/products/custom-sand-soil-blend",
        "Custom Sand Soil Blend",
        "Custom Sand & Soil Blends | Soil Mixes for Lawns, Garden & Landscaping – Green Hills Supply",
        "Custom Sand Soil Blend",
        "Lawns & Gardens")]
    [InlineData(
        "https://www.greenhillssupply.com/products/top-soil",
        "Lawn & Garden Topsoil",
        "Lawn & Garden Topsoil | Premium Topsoil for Seeding, Sod & Lawn Repair – Green Hills Supply",
        "Lawn & Garden Topsoil",
        "Seeding, Sod & Lawn Repair")]
    public void TitleLength_PreservesProductIntentInTailoredSuggestion(
        string url,
        string heading,
        string currentTitle,
        string expectedTopic,
        string expectedIntent)
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.TitleLength(
                new Uri(url),
                heading,
                currentTitle);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.InRange(recommendation.SuggestedValue.Length, 20, 60);
        Assert.Contains(
            expectedTopic,
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            expectedIntent,
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            @"(?:\band\b|\bfor\b|\bwith\b|&|\|)$",
            recommendation.SuggestedValue);
    }

    [Theory]
    [InlineData(
        "https://cdn.shopify.com/files/AlpineStone_2.png",
        "alpine stone")]
    [InlineData(
        "https://cdn.shopify.com/files/Alpine_Stone_Medium_1.png",
        "alpine stone")]
    [InlineData(
        "https://cdn.shopify.com/files/Alpine_Stone_Large_e23088e6-0a03-45ed-b684-6eb05c1c38ff.png",
        "alpine stone")]
    [InlineData(
        "https://cdn.shopify.com/files/American_Heritage_Large.png",
        "american heritage")]
    public void ImageAltQuality_NormalizesResponsiveVariantFilenames(
        string source,
        string expected)
    {
        Assert.Equal(
            expected,
            WebsiteHealthMonitorService.GetLogicalImageSubjectKey(source));
    }

    [Fact]
    public void IssueLifecycle_DoesNotResolveAnUnvisitedFinding()
    {
        var issue = "title-length|product-a";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evaluated = new HashSet<string>(
            ["title-length|product-b"],
            StringComparer.OrdinalIgnoreCase);

        Assert.False(
            WebsiteHealthMonitorService.ShouldResolveIssue(
                issue,
                seen,
                evaluated));
    }

    [Fact]
    public void IssueLifecycle_ResolvesARecheckedPassingFinding()
    {
        var issue = "title-length|product-a";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var evaluated = new HashSet<string>(
            [issue],
            StringComparer.OrdinalIgnoreCase);

        Assert.True(
            WebsiteHealthMonitorService.ShouldResolveIssue(
                issue,
                seen,
                evaluated));
    }

    [Fact]
    public void IssueLifecycle_KeepsARecheckedFailingFindingOpen()
    {
        var issue = "title-length|product-a";
        var seen = new HashSet<string>(
            [issue],
            StringComparer.OrdinalIgnoreCase);
        var evaluated = new HashSet<string>(
            [issue],
            StringComparer.OrdinalIgnoreCase);

        Assert.False(
            WebsiteHealthMonitorService.ShouldResolveIssue(
                issue,
                seen,
                evaluated));
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

        Assert.NotNull(aggregate.SuggestedValue);
        Assert.NotNull(mulch.SuggestedValue);
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
        Assert.False(
            mulch.SuggestedValue.EndsWith(
                "convenient.",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MetaDescriptionLength_LeadsWithTheProductIdentifyingSentence()
    {
        const string current =
            "Floral Natural Cedar Mulch is a convenient 2 cu. ft. bagged mulch that adds a clean, natural cedar finish to garden beds, tree rings, and landscape borders. It helps retain moisture, suppress weeds, and protect soil while giving small projects and seasonal touch-ups a polished look.";

        var recommendation =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/products/bagged-natural-cedar-mulch"),
                "Floral Natural Cedar Mulch",
                "Floral Natural Cedar Mulch",
                null,
                current);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.StartsWith(
            "Floral Natural Cedar Mulch",
            recommendation.SuggestedValue);
        Assert.False(recommendation.SuggestedValue.StartsWith(
            "It helps",
            StringComparison.OrdinalIgnoreCase));
        Assert.InRange(recommendation.SuggestedValue.Length, 70, 155);
    }

    [Fact]
    public void MetaDescriptionLength_DoesNotSplitMeasurementAbbreviations()
    {
        const string current =
            "Bagged Traffic Bond (1/2 cu. ft. bag) is a compactable crushed stone base material ideal for small repairs, leveling, and touch-up projects. It packs down tight to create a firm, stable surface for driveway patching and walkway preparation.";

        var recommendation =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/products/bagged-traffic-bond-gravel"),
                "Bagged Traffic Bond",
                "Bagged Traffic Bond",
                null,
                current);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.StartsWith(
            "Bagged Traffic Bond",
            recommendation.SuggestedValue);
        Assert.False(recommendation.SuggestedValue.StartsWith(
            "bag)",
            StringComparison.OrdinalIgnoreCase));
        Assert.InRange(recommendation.SuggestedValue.Length, 70, 155);
    }

    [Theory]
    [InlineData(
        "DISCOVER PAVERS® offer a smooth, sought-after surface texture in traditional sizes.Manufactured as a three piece module, these pavers can be used alone or melded withcompatible pavers to create one-of-a-kind installations.",
        "sizes.",
        "sizes.Manufactured")]
    [InlineData(
        "Grand DISCOVER PAVERS® offer a smooth, sought-after surface texture inlarger slab sizes. Manufactured as a three-piece module, these pavers can be usedalone or melded with compatible pavers.",
        "in larger",
        "inlarger")]
    public void MetaDescriptionLength_CleansSourceSpacingDefects(
        string current,
        string expected,
        string unexpected)
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                new Uri(
                    "https://www.greenhillssupply.com/products/discover-pavers"),
                "Discover Pavers",
                "Discover Pavers",
                null,
                current);

        Assert.NotNull(recommendation.SuggestedValue);
        Assert.Contains(
            expected,
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            unexpected,
            recommendation.SuggestedValue,
            StringComparison.OrdinalIgnoreCase);
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
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "https://www.greenhillssupply.com/collections/mulch",
        true)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch?page=2",
        "https://www.greenhillssupply.com/collections/mulch?page=2",
        true)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "https://greenhillssupply.com/collections/mulch",
        true)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "http://www.greenhillssupply.com/collections/mulch",
        false)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "https://example.com/collections/mulch",
        false)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "https://www.greenhillssupply.com/collections/stone",
        false)]
    [InlineData(
        "https://www.greenhillssupply.com/collections/mulch",
        "https://www.greenhillssupply.com/collections/mulch?utm_source=email",
        false)]
    public void IsCanonicalHealthy_ValidatesSecureSelfReferencingUrls(
        string pageUrl,
        string canonical,
        bool expected)
    {
        var result = WebsiteHealthMonitorService.IsCanonicalHealthy(
            new Uri(pageUrl),
            canonical);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void HeadingStructure_UsesTheCurrentPageTopicAndShopifyTemplate()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.HeadingStructure(
                new Uri(
                    "https://www.greenhillssupply.com/collections/mulch"),
                "Mulch | Green Hills Supply",
                null,
                0);

        Assert.Equal(
            "<h1>Mulch</h1>",
            recommendation.SuggestedValue);
        Assert.Contains(
            "collection template",
            recommendation.FixLocation);
    }

    [Fact]
    public void HeadingStructure_TreatsAnEmptyH1AsMissing()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.HeadingStructure(
                new Uri("https://www.greenhillssupply.com/"),
                "Green Hills Supply | Landscape & Outdoor Materials",
                "",
                1);

        Assert.Equal(
            "<h1>Green Hills Supply | Landscape &amp; Outdoor Materials</h1>",
            recommendation.SuggestedValue);
        Assert.Contains(
            "no readable text",
            recommendation.CurrentValue);
    }

    [Fact]
    public void MeaningfulHeadingText_UsesLogoAltTextWhenH1HasNoText()
    {
        var heading = WebsiteHealthMonitorService.GetMeaningfulHeadingText(
            "  ",
            null,
            ["Green Hills Supply", "Green Hills Supply"]);

        Assert.Equal("Green Hills Supply", heading);
    }

    [Fact]
    public void AccessibilityHeading_PointsToTheActualRichTextTemplate()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.HeadingStructure(
                new Uri(
                    "https://www.greenhillssupply.com/pages/accessibility"),
                "Accessibility Statement – Green Hills Supply",
                null,
                0);

        Assert.Contains(
            "Pages → Accessibility",
            recommendation.FixLocation);
        Assert.Contains(
            "Rich text",
            recommendation.FixLocation);
        Assert.Equal(
            "<h1 class=\"h2 text-center\">Accessibility Statement – Green Hills Supply</h1>",
            recommendation.SuggestedValue);
    }

    [Fact]
    public void SearchIndexability_PreservesTheCurrentRobotsDirective()
    {
        var recommendation =
            WebsiteHealthRecommendationBuilder.SearchIndexability(
                new Uri(
                    "https://www.greenhillssupply.com/products/limestone"),
                "noindex,nofollow");

        Assert.Equal(
            "noindex,nofollow",
            recommendation.CurrentValue);
        Assert.Equal(
            """<meta name="robots" content="index,follow">""",
            recommendation.SuggestedValue);
        Assert.Contains(
            "Products → open this product",
            recommendation.FixLocation);
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
