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
}
