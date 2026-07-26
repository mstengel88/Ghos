using System.Net;
using System.Text.RegularExpressions;

namespace Ghos.Web.Shopify;

public static partial class ShopifyProductText
{
    public static string? FromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var withBreaks = BreakRegex().Replace(html, "\n");
        var withoutTags = TagRegex().Replace(withBreaks, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var cleaned = WhitespaceRegex().Replace(decoded, " ").Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    public static string? ToShortDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        return description.Length <= 320
            ? description
            : $"{description[..317].TrimEnd()}…";
    }

    [GeneratedRegex(@"<(br|/p|/li|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
