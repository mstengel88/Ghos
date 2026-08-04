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
    public void NormalizeUrl_RemovesFragmentAndTrailingSlash()
    {
        var normalized = WebsiteHealthMonitorService.NormalizeUrl(
            new Uri("https://example.com/products/stone/?variant=1#details"));

        Assert.Equal(
            "https://example.com/products/stone/?variant=1",
            normalized);
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
