using System.Net;
using Ghos.Web.WebsiteHealth;
using Xunit;

namespace Ghos.Web.Tests;

public sealed class WebsiteHealthMonitorServiceTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.10.2", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("2606:4700:4700::1111", false)]
    [InlineData("fc00::1", true)]
    public void IsPrivateAddress_ClassifiesNetworkRanges(
        string input,
        bool expected)
    {
        var result = WebsiteHealthMonitorService.IsPrivateAddress(
            IPAddress.Parse(input));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseRobotsDisallowRules_UsesWildcardAgentRules()
    {
        const string robots =
            """
            User-agent: SearchBot
            Disallow: /search-bot-only

            User-agent: *
            Disallow: /cart
            Disallow: /account/
            # Disallow: /commented-out
            """;

        var rules =
            WebsiteHealthMonitorService.ParseRobotsDisallowRules(robots);

        Assert.Equal(["/cart", "/account/"], rules);
    }

    [Theory]
    [InlineData("https://example.com/cart", true)]
    [InlineData("https://example.com/account/login", true)]
    [InlineData("https://example.com/products/stone", false)]
    public void IsDisallowed_MatchesRobotsPathPrefixes(
        string input,
        bool expected)
    {
        var result = WebsiteHealthMonitorService.IsDisallowed(
            new Uri(input),
            ["/cart", "/account/"]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseRobotsSitemapLocations_FindsAbsoluteDeclarations()
    {
        const string robots =
            """
            User-agent: *
            Disallow: /cart
            Sitemap: https://example.com/sitemap.xml
            Sitemap: not-a-url
            """;

        var locations =
            WebsiteHealthMonitorService.ParseRobotsSitemapLocations(
                robots);

        Assert.Equal(
            [new Uri("https://example.com/sitemap.xml")],
            locations);
    }

    [Fact]
    public void AnalyzeSitemap_AcceptsShopifySitemapIndex()
    {
        const string sitemap =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap>
                <loc>https://example.com/sitemap_products_1.xml</loc>
              </sitemap>
              <sitemap>
                <loc>https://www.example.com/sitemap_collections_1.xml</loc>
              </sitemap>
            </sitemapindex>
            """;

        var analysis = WebsiteHealthMonitorService.AnalyzeSitemap(
            sitemap,
            new Uri("https://www.example.com"));

        Assert.True(analysis.IsValidXml);
        Assert.True(analysis.HasSupportedRoot);
        Assert.Equal(2, analysis.LocationCount);
        Assert.Equal(0, analysis.InvalidLocationCount);
        Assert.Equal(0, analysis.ExternalLocationCount);
    }

    [Fact]
    public void AnalyzeSitemap_ReportsMalformedAndExternalLocations()
    {
        const string sitemap =
            """
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url><loc>http://example.com/products/stone</loc></url>
              <url><loc>https://other.example/products/mulch</loc></url>
            </urlset>
            """;

        var analysis = WebsiteHealthMonitorService.AnalyzeSitemap(
            sitemap,
            new Uri("https://example.com"));

        Assert.True(analysis.IsValidXml);
        Assert.Equal(1, analysis.InvalidLocationCount);
        Assert.Equal(1, analysis.ExternalLocationCount);
    }

    [Fact]
    public void ParseSitemapDocument_ExtractsShopifyCustomerPages()
    {
        const string sitemap =
            """
            <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <url>
                <loc>https://example.com/products/alpine-stone</loc>
              </url>
              <url>
                <loc>https://example.com/collections/mulch</loc>
              </url>
            </urlset>
            """;

        var document =
            WebsiteHealthMonitorService.ParseSitemapDocument(sitemap);

        Assert.True(document.IsValidXml);
        Assert.Equal("urlset", document.RootName);
        Assert.Equal(
            [
                "https://example.com/products/alpine-stone",
                "https://example.com/collections/mulch"
            ],
            document.Locations);
    }

    [Fact]
    public void OrderCrawlTargets_PrioritizesUnvisitedProducts()
    {
        var previouslyChecked =
            new Uri("https://example.com/products/alpine-stone");
        var unseenProduct =
            new Uri("https://example.com/products/american-heritage");
        var unseenCollection =
            new Uri("https://example.com/collections/mulch");
        var lastEvaluated = new Dictionary<string, DateTime>(
            StringComparer.OrdinalIgnoreCase)
        {
            [WebsiteHealthMonitorService.NormalizeUrl(previouslyChecked)] =
                DateTime.UtcNow
        };

        var ordered = WebsiteHealthMonitorService.OrderCrawlTargets(
            [previouslyChecked, unseenCollection, unseenProduct],
            lastEvaluated);

        Assert.Equal(unseenProduct, ordered[0]);
        Assert.Equal(previouslyChecked, ordered[1]);
        Assert.Equal(unseenCollection, ordered[2]);
    }

    [Fact]
    public void NormalizeUrl_RemovesFragmentAndTrailingSlash()
    {
        var normalized = WebsiteHealthMonitorService.NormalizeUrl(
            new Uri("https://example.com/products/stone/?variant=1#details"));

        Assert.Equal(
            "https://example.com/products/stone/?variant=1",
            normalized);
    }

    [Theory]
    [InlineData(
        "https://www.example.com/products/stone",
        "https://example.com/products/stone",
        1,
        true)]
    [InlineData(
        "https://example.com/products/stone",
        "https://example.com/products/stone",
        0,
        true)]
    [InlineData(
        "https://example.com/products/stone",
        "https://example.com/collections/stone",
        1,
        false)]
    [InlineData(
        "https://example.com/products/stone",
        "https://example.com/products/stone",
        2,
        false)]
    public void IsRedirectChainHealthy_AllowsOnlyOneCanonicalHostRedirect(
        string requestedUrl,
        string finalUrl,
        int redirectCount,
        bool expected)
    {
        var healthy =
            WebsiteHealthMonitorService.IsRedirectChainHealthy(
                new Uri(requestedUrl),
                new Uri(finalUrl),
                redirectCount);

        Assert.Equal(expected, healthy);
    }

    [Fact]
    public void AnalyzeSecurityHeaders_AcceptsShopifyProtections()
    {
        var analysis =
            WebsiteHealthMonitorService.AnalyzeSecurityHeaders(
                new Dictionary<string, string>
                {
                    ["strict-transport-security"] = "max-age=7889238",
                    ["x-content-type-options"] = "nosniff",
                    ["x-frame-options"] = "DENY",
                    ["content-security-policy"] =
                        "block-all-mixed-content; frame-ancestors 'none'"
                });

        Assert.True(analysis.IsHealthy);
        Assert.Empty(analysis.MissingHeaders);
    }

    [Fact]
    public void AnalyzeSecurityHeaders_AcceptsCspFramingWithoutXFrameOptions()
    {
        var analysis =
            WebsiteHealthMonitorService.AnalyzeSecurityHeaders(
                new Dictionary<string, string>
                {
                    ["Strict-Transport-Security"] = "max-age=31536000",
                    ["X-Content-Type-Options"] = "nosniff",
                    ["Content-Security-Policy"] =
                        "default-src 'self'; frame-ancestors 'self'"
                });

        Assert.True(analysis.HasFramingProtection);
        Assert.True(analysis.IsHealthy);
    }

    [Fact]
    public void AnalyzeSecurityHeaders_ReportsMissingOrDisabledProtections()
    {
        var analysis =
            WebsiteHealthMonitorService.AnalyzeSecurityHeaders(
                new Dictionary<string, string>
                {
                    ["Strict-Transport-Security"] = "max-age=0",
                    ["X-Content-Type-Options"] = "invalid"
                });

        Assert.False(analysis.IsHealthy);
        Assert.Contains(
            "Strict-Transport-Security",
            analysis.MissingHeaders);
        Assert.Contains(
            "X-Content-Type-Options: nosniff",
            analysis.MissingHeaders);
        Assert.Contains(
            "framing protection",
            analysis.MissingHeaders);
        Assert.Contains(
            "Content-Security-Policy",
            analysis.MissingHeaders);
    }

    [Theory]
    [InlineData("https://example.com/products/stone", 0)]
    [InlineData("https://example.com/collections/aggregate", 1)]
    [InlineData("https://example.com/pages/contact", 2)]
    [InlineData("https://example.com/blogs/news/stone-guide", 2)]
    [InlineData("https://example.com/policies/privacy-policy", 3)]
    public void GetCrawlPriority_CoversCommercePagesBeforeUtilityContent(
        string url,
        int expected)
    {
        Assert.Equal(
            expected,
            WebsiteHealthMonitorService.GetCrawlPriority(
                new Uri(url)));
    }

    [Fact]
    public void GetMissingSocialPreviewFields_ReportsOnlyMissingOrInvalidValues()
    {
        var missing =
            WebsiteHealthMonitorService.GetMissingSocialPreviewFields(
                "Driveway Stone | Green Hills Supply",
                "",
                "/images/stone.jpg",
                "http://example.com/products/stone",
                "summary_large_image");

        Assert.Equal(
            ["og:description", "og:image", "og:url"],
            missing);
    }

    [Fact]
    public void GetMissingSocialPreviewFields_AcceptsCompletePreview()
    {
        var missing =
            WebsiteHealthMonitorService.GetMissingSocialPreviewFields(
                "Driveway Stone | Green Hills Supply",
                "Shop driveway stone for a durable landscape project.",
                "https://cdn.example.com/stone.jpg",
                "https://example.com/products/stone",
                "summary_large_image");

        Assert.Empty(missing);
    }
}
