using System.Net;
using System.Text;
using System.Text.Json;

namespace Ghos.Web.WebsiteHealth;

internal sealed record WebsiteHealthRecommendation(
    string Guidance,
    string? SuggestedValue,
    string? FixLocation = null);

internal sealed record WebsiteHealthMissingImage(
    string? Source,
    string? Context,
    string? PageUrl = null);

internal static class WebsiteHealthRecommendationBuilder
{
    private const string BrandName = "Green Hills Supply";

    internal static WebsiteHealthRecommendation MissingTitle(
        Uri url,
        string? heading)
    {
        var topic = GetPageTopic(url, heading, null);
        var title = url.AbsolutePath == "/"
            ? "Green Hills Supply | Landscape & Outdoor Materials"
            : $"{topic} | {BrandName}";

        return new WebsiteHealthRecommendation(
            "Add one concise, unique HTML title that leads with the page topic and ends with the Green Hills Supply brand. Keep it near 50–60 characters and avoid repeating the same title on other pages.",
            TruncateAtWord(title, 60),
            GetShopifySeoLocation(url));
    }

    internal static WebsiteHealthRecommendation MissingMetaDescription(
        Uri url,
        string? title,
        string? heading,
        string? introductoryText)
    {
        if (IsUtilityPage(url))
        {
            return new WebsiteHealthRecommendation(
                "This is a utility page, so it should not compete in search results. Add a noindex directive instead of marketing copy; the monitor will stop expecting a meta description once the page is intentionally excluded from indexing.",
                """<meta name="robots" content="noindex,follow">""",
                "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head> with a cart/search condition");
        }

        var topic = GetPageTopic(url, heading, title);
        var description = BuildDescription(
            url,
            topic,
            introductoryText);
        return new WebsiteHealthRecommendation(
            "Add a unique meta description that explains what a customer will find on this page and gives them a reason to click. Aim for roughly 120–155 characters and do not reuse it across paginated or related pages.",
            description,
            GetShopifySeoLocation(url));
    }

    internal static WebsiteHealthRecommendation MissingCanonical(Uri url)
    {
        var canonical = new UriBuilder(url)
        {
            Fragment = string.Empty
        }.Uri.GetLeftPart(UriPartial.Path);

        return new WebsiteHealthRecommendation(
            "Add a self-referencing canonical URL in the page head. Remove tracking and pagination parameters unless this page intentionally represents a distinct indexable result.",
            $"""<link rel="canonical" href="{WebUtility.HtmlEncode(canonical)}">""",
            "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head>");
    }

    internal static WebsiteHealthRecommendation MissingSchema(
        Uri url,
        string? title,
        string? heading,
        string? metaDescription)
    {
        var topic = GetPageTopic(url, heading, title);
        var description = string.IsNullOrWhiteSpace(metaDescription)
            ? BuildDescription(url, topic, null)
            : NormalizeText(metaDescription);
        var schema = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = url.AbsolutePath == "/" ? "WebSite" : "WebPage",
                ["name"] = topic,
                ["url"] = url.GetLeftPart(UriPartial.Path),
                ["description"] = description
            },
            new JsonSerializerOptions { WriteIndented = true });

        return new WebsiteHealthRecommendation(
            "Add valid JSON-LD that describes this page. The suggested WebPage markup is a safe baseline; product pages should be expanded with Product, Offer, price, availability, image, SKU, and review data from Shopify.",
            $"<script type=\"application/ld+json\">\n{schema}\n</script>",
            "Shopify Admin → Online Store → Themes → … → Edit code → the relevant template or structured-data snippet");
    }

    internal static WebsiteHealthRecommendation MissingImageAltText(
        Uri pageUrl,
        string? title,
        string? heading,
        IReadOnlyList<WebsiteHealthMissingImage> images)
    {
        var topic = GetPageTopic(pageUrl, heading, title);
        var generatedSuggestions = images
            .Take(8)
            .Select((image, index) =>
            {
                var sourceLabel = GetImageLabel(
                    image.Source,
                    image.PageUrl,
                    index + 1);
                var altText = BuildImageAltText(
                    topic,
                    image.Context,
                    image.Source);
                return $"{sourceLabel}: alt=\"{altText}\"";
            })
            .ToList();
        var suggestions = generatedSuggestions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (generatedSuggestions.Count > suggestions.Count)
        {
            suggestions.Add(
                $"{generatedSuggestions.Count - suggestions.Count} repeated image occurrence(s) should use the same alt text");
        }

        if (images.Count > generatedSuggestions.Count)
        {
            suggestions.Add(
                $"+ {images.Count - generatedSuggestions.Count} more image(s) to review");
        }

        return new WebsiteHealthRecommendation(
            "Give each meaningful image short alt text describing what the customer needs to understand. Use alt=\"\" only for genuinely decorative images, and avoid phrases such as “image of” or keyword stuffing.",
            string.Join(Environment.NewLine, suggestions),
            "Shopify Admin → open the product, collection, page, or theme section that owns this image → edit the image alt text");
    }

    internal static WebsiteHealthRecommendation BrokenLink(
        Uri target,
        int? statusCode)
    {
        var status = statusCode is null
            ? "did not respond"
            : $"returned HTTP {statusCode}";
        return new WebsiteHealthRecommendation(
            $"Find every page linking to this target. Because it {status}, update the link to the closest live replacement, add a permanent redirect when the content moved, or remove the link when no replacement exists.",
            target.ToString(),
            "Shopify Admin → Content or Online Store → Navigation/Pages/Themes → open the content containing this link");
    }

    internal static WebsiteHealthRecommendation AvailabilityFailure(
        string key,
        Uri target)
    {
        var guidance = key switch
        {
            "homepage" =>
                "Restore the homepage before addressing content warnings. Confirm DNS, CDN/origin availability, application health, and the latest deployment, then rerun the check.",
            "robots" =>
                "Publish a plain-text robots.txt at the site root. Allow public storefront pages, disallow private utility routes, and include the production sitemap URL.",
            "sitemap" =>
                "Publish a valid XML sitemap at this URL and include only canonical, indexable pages that return HTTP 200.",
            "ssl" =>
                "Renew or replace the TLS certificate, confirm the full certificate chain, and verify the hostname before it expires or customer traffic is affected.",
            _ =>
                "Confirm the page still belongs at this address. Restore it, redirect it to the correct replacement, or update navigation and monitoring if the page was intentionally removed."
        };
        var suggestedValue = key switch
        {
            "robots" =>
                $"User-agent: *\nAllow: /\nSitemap: {new Uri(target, "/sitemap.xml")}",
            _ => target.ToString()
        };

        var fixLocation = key switch
        {
            "robots" =>
                "Shopify Admin → Online Store → Themes → … → Edit code → templates/robots.txt.liquid",
            "sitemap" =>
                "Shopify generates sitemap.xml automatically; review product/page publishing and contact Shopify Support if the route is unavailable",
            "ssl" =>
                "Shopify Admin → Settings → Domains → open the production domain",
            _ =>
                "Shopify Admin → Online Store and Domains, plus the current theme/deployment status"
        };

        return new WebsiteHealthRecommendation(
            guidance,
            suggestedValue,
            fixLocation);
    }

    private static string BuildDescription(
        Uri url,
        string topic,
        string? introductoryText)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        string description;
        if (path.Length == 0)
        {
            description =
                "Shop stone, mulch, soil, sand, salt, landscape supplies, and outdoor materials from Green Hills Supply, with convenient pickup and delivery options.";
        }
        else if (path.Equals("/blogs/news", StringComparison.OrdinalIgnoreCase))
        {
            description =
                "Read landscaping tips, material guides, seasonal advice, and company updates from the team at Green Hills Supply.";
        }
        else if (path.StartsWith(
            "/collections/",
            StringComparison.OrdinalIgnoreCase))
        {
            var pageNumber = GetQueryValue(url, "page");
            if (path.Equals(
                "/collections/all",
                StringComparison.OrdinalIgnoreCase))
            {
                description = string.IsNullOrWhiteSpace(pageNumber)
                    ? "Browse landscape and outdoor products from Green Hills Supply, including materials for landscaping, property care, and seasonal projects."
                    : $"Browse page {pageNumber} of Green Hills Supply's landscape and outdoor products for landscaping, property care, and seasonal projects.";
            }
            else
            {
                description = string.IsNullOrWhiteSpace(pageNumber)
                    ? $"Shop {topic} from Green Hills Supply. Find quality materials and products for landscaping, property care, and seasonal projects."
                    : $"Browse page {pageNumber} of {topic} from Green Hills Supply, with quality products for landscaping, property care, and seasonal projects.";
            }
        }
        else if (path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase))
        {
            description = GetCompleteIntroductorySentence(
                    introductoryText) ??
                $"Shop {topic} from Green Hills Supply. View product details, recommended uses, and convenient pickup or delivery options.";
        }
        else
        {
            description = GetCompleteIntroductorySentence(
                    introductoryText) ??
                $"Learn about {topic} from Green Hills Supply, with helpful information for customers planning landscape and property projects.";
        }

        return EnsureSentence(TruncateAtWord(description, 155));
    }

    private static string GetPageTopic(
        Uri url,
        string? heading,
        string? title)
    {
        var candidate = FirstMeaningful(heading, title);
        if (!string.IsNullOrWhiteSpace(candidate))
        {
            var withoutBrand = candidate.Replace(
                $"| {BrandName}",
                "",
                StringComparison.OrdinalIgnoreCase);
            return TruncateAtWord(NormalizeText(withoutBrand), 70);
        }

        if (url.AbsolutePath == "/")
        {
            return BrandName;
        }

        var slug = url.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "Website";
        return HumanizeSlug(slug);
    }

    private static string BuildImageAltText(
        string pageTopic,
        string? context,
        string? source)
    {
        var normalizedContext = NormalizeText(context);
        if (normalizedContext.Length is >= 3 and <= 100 &&
            !LooksGeneric(normalizedContext))
        {
            return TruncateAtWord(normalizedContext, 125);
        }

        var filename = GetFilenameTopic(source);
        if (!string.IsNullOrWhiteSpace(filename) &&
            !LooksGeneric(filename))
        {
            return TruncateAtWord(filename, 125);
        }

        return TruncateAtWord(pageTopic, 125);
    }

    private static string GetImageLabel(
        string? source,
        string? pageUrl,
        int number)
    {
        var absoluteSource = source?.StartsWith(
            "//",
            StringComparison.Ordinal) == true
            ? $"https:{source}"
            : source;
        if (!Uri.TryCreate(absoluteSource, UriKind.Absolute, out var uri))
        {
            return $"Image {number}";
        }

        var filename = Path.GetFileName(uri.AbsolutePath);
        var label = string.IsNullOrWhiteSpace(filename)
            ? $"Image {number}"
            : WebUtility.UrlDecode(filename);
        if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page))
        {
            label += $" on {page.PathAndQuery}";
        }

        return label;
    }

    private static string? GetFilenameTopic(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var path = Uri.TryCreate(source, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : source;
        var filename = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(filename)
            ? null
            : HumanizeSlug(filename);
    }

    private static string HumanizeSlug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = true;
        foreach (var character in WebUtility.UrlDecode(value))
        {
            if (character is '-' or '_' or '.')
            {
                if (!previousWasSeparator)
                {
                    builder.Append(' ');
                }

                previousWasSeparator = true;
                continue;
            }

            if (char.IsDigit(character))
            {
                continue;
            }

            builder.Append(character);
            previousWasSeparator = false;
        }

        var normalized = NormalizeText(builder.ToString());
        return string.IsNullOrWhiteSpace(normalized)
            ? "Website"
            : string.Join(
                ' ',
                normalized.Split(' ').Select(word =>
                    word.Length <= 3 && word.All(char.IsUpper)
                        ? word
                        : char.ToUpperInvariant(word[0]) +
                            word[1..].ToLowerInvariant()));
    }

    private static string NormalizeText(string? value) =>
        string.Join(
            ' ',
            WebUtility.HtmlDecode(value ?? "")
                .Split(
                    [' ', '\r', '\n', '\t'],
                    StringSplitOptions.RemoveEmptyEntries));

    private static string? FirstMeaningful(params string?[] values) =>
        values
            .Select(NormalizeText)
            .FirstOrDefault(value => value.Length >= 3);

    internal static bool IsUtilityPage(Uri url) =>
        url.AbsolutePath.Equals("/cart", StringComparison.OrdinalIgnoreCase) ||
        url.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase);

    internal static string GetShopifySeoLocation(Uri url)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            return "Shopify Admin → Online Store → Preferences → Social sharing image and SEO";
        }

        if (path.Equals(
            "/collections/all",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Products → Collections → create or open All → Search engine listing → Edit website SEO; keep the handle /all";
        }

        if (path.StartsWith(
            "/collections/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Products → Collections → open this collection → Search engine listing → Edit website SEO";
        }

        if (path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Products → open this product → Search engine listing → Edit";
        }

        if (path.Equals("/blogs/news", StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head> for the blog index";
        }

        if (path.StartsWith("/blogs/", StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Content → Blog posts → open this post → Search engine listing → Edit";
        }

        if (path.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Online Store → Pages → open this page → Search engine listing → Edit website SEO";
        }

        return "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head>";
    }

    private static bool LooksGeneric(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized is "image" or "photo" or "picture" or "thumbnail" ||
            normalized.Contains("untitled design", StringComparison.Ordinal) ||
            normalized.StartsWith("img", StringComparison.Ordinal) ||
            normalized.StartsWith("dsc", StringComparison.Ordinal);
    }

    private static string TruncateAtWord(string value, int maximumLength)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var cutoff = normalized.LastIndexOf(' ', maximumLength - 1);
        return normalized[..(cutoff > maximumLength / 2
            ? cutoff
            : maximumLength)].TrimEnd(' ', ',', ';', ':', '-', '.');
    }

    private static string EnsureSentence(string value) =>
        value.EndsWith('.') ||
        value.EndsWith('!') ||
        value.EndsWith('?')
            ? value
            : $"{value}.";

    private static string? GetCompleteIntroductorySentence(string? value)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length < 80)
        {
            return null;
        }

        if (normalized.Length <= 155)
        {
            return EnsureSentence(normalized);
        }

        var punctuationIndex = normalized.IndexOfAny(['.', '!', '?']);
        if (punctuationIndex is >= 79 and < 155)
        {
            return normalized[..(punctuationIndex + 1)];
        }

        return null;
    }

    private static string? GetQueryValue(Uri url, string key)
    {
        foreach (var pair in url.Query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(parts[1]);
            }
        }

        return null;
    }
}
