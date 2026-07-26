using System.Text.RegularExpressions;

namespace Ghos.Web.Products;

public static partial class ProductSlug
{
    public static string Create(string value)
    {
        var slug = NonAlphaNumericRegex()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug) ? "product" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();
}
