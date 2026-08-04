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
