using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ghos.Web.WebsiteHealth;

internal sealed record WebsiteHealthRecommendation(
    string Guidance,
    string? SuggestedValue,
    string? FixLocation = null,
    string? DocumentationUrl = null,
    string? CurrentValue = null,
    string? EvidenceJson = null);

internal sealed record WebsiteHealthImageEvidence(
    string? SourceUrl,
    string? PageUrl,
    string? CurrentAltText,
    string SuggestedAltText,
    string SuggestionSource);

internal sealed record WebsiteHealthMissingImage(
    string? Source,
    string? Context,
    string? PageUrl = null);

internal sealed record WebsiteHealthImage(
    string? Source,
    string AltText,
    string? Context,
    string? PageUrl = null,
    bool IsBrandLogo = false);

internal static class WebsiteHealthRecommendationBuilder
{
    private const string BrandName = "Green Hills Supply";
    private const string AllCatalogMetaDescription =
        "Shop Green Hills Supply for aggregate, decorative stone, mulch, sand, bagged landscape materials, tools, ice melt, and water-conditioning products.";

    internal static WebsiteHealthRecommendation MissingTitle(
        Uri url,
        string? heading)
    {
        return new WebsiteHealthRecommendation(
            "Add one concise, unique HTML title that leads with the page topic and ends with the Green Hills Supply brand. Keep it near 50–60 characters and avoid repeating the same title on other pages.",
            BuildTitle(url, heading),
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url));
    }

    internal static WebsiteHealthRecommendation TitleLength(
        Uri url,
        string? heading,
        string currentTitle)
    {
        var currentLength = NormalizeText(currentTitle).Length;
        var problem = currentLength < 20
            ? "The current title is too short to explain the page clearly in search results."
            : "The current title is likely to be truncated in search results.";
        return new WebsiteHealthRecommendation(
            $"{problem} The suggested title is condensed from the current live title, preserving the product, material, and strongest customer use instead of replacing it with generic copy. It includes the Green Hills Supply brand when the useful detail still fits.",
            BuildTailoredTitle(url, heading, currentTitle),
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url),
            NormalizeText(currentTitle));
    }

    internal static WebsiteHealthRecommendation DuplicateTitle(
        Uri url,
        string? heading,
        Uri matchingUrl)
    {
        return new WebsiteHealthRecommendation(
            $"This title also appears on {matchingUrl.PathAndQuery}. Give this page a distinct title based on its own subject so search engines and customers can tell the pages apart.",
            BuildTitle(url, heading),
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url));
    }

    internal static WebsiteHealthRecommendation HeadingStructure(
        Uri url,
        string? title,
        string? heading,
        int headingCount)
    {
        var topic = GetPageTopic(url, heading, title);
        var isMissingOrEmpty = headingCount <= 1 &&
            string.IsNullOrWhiteSpace(heading);
        var guidance = isMissingOrEmpty
            ? "Add one visible H1 heading that names this page. The wording below is tailored from the current page title and URL; confirm it matches what customers see."
            : $"Keep one H1 heading for “{topic}” and change the other {headingCount - 1} H1 heading{(headingCount == 2 ? "" : "s")} to H2 or H3. Do not hide duplicate headings with CSS.";
        var suggestedValue = isMissingOrEmpty
            ? url.AbsolutePath.Equals(
                "/pages/accessibility",
                StringComparison.OrdinalIgnoreCase)
                ? $"<h1 class=\"h2 text-center\">{WebUtility.HtmlEncode(topic)}</h1>"
                : $"<h1>{WebUtility.HtmlEncode(topic)}</h1>"
            : $"Keep as H1: \"{topic}\"\nChange the other {headingCount - 1} H1 heading{(headingCount == 2 ? "" : "s")} to H2 or H3.";
        var currentValue = headingCount switch
        {
            0 => "No H1 element detected.",
            1 when string.IsNullOrWhiteSpace(heading) =>
                "One H1 element detected, but it has no readable text.",
            _ => $"{headingCount} H1 elements detected. First H1: {heading}"
        };

        return new WebsiteHealthRecommendation(
            guidance,
            suggestedValue,
            GetShopifyHeadingLocation(url),
            "https://help.shopify.com/en/manual/online-store/themes/customizing-themes",
            currentValue);
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

        var isShopifyCatalog = url.AbsolutePath.Equals(
            "/collections/all",
            StringComparison.OrdinalIgnoreCase);
        var topic = GetPageTopic(url, heading, title);
        var description = isShopifyCatalog
            ? AllCatalogMetaDescription
            : BuildDescription(
                url,
                topic,
                introductoryText);
        var guidance = isShopifyCatalog
            ? "This is Shopify's built-in catalog URL, which exists even when no All collection appears under Products → Collections. Green Hills Supply uses Shopify's new Collections experience, which no longer has separate Smart and Manual collection types. Automatic membership is now configured by adding conditions to the collection's Products source. A separate All collection does not replace your separate collections landing page. Do not rename another collection, your collections landing page, or any menu link."
            : "Add a unique meta description that explains what a customer will find on this page and gives them a reason to click. Aim for roughly 120–155 characters and do not reuse it across paginated or related pages.";
        return new WebsiteHealthRecommendation(
            guidance,
            description,
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url));
    }

    internal static WebsiteHealthRecommendation MetaDescriptionLength(
        Uri url,
        string? title,
        string? heading,
        string? introductoryText,
        string currentDescription)
    {
        var currentLength = NormalizeText(currentDescription).Length;
        var problem = currentLength < 70
            ? "The current meta description is too short to communicate the page's value in search results."
            : "The current meta description is likely to be truncated in search results.";
        var sourceExplanation = currentLength < 70
            ? "The suggestion keeps the useful existing wording and expands it with page-specific context."
            : "The suggestion is condensed from the current live description, preserving its specific products, uses, and benefits.";
        return new WebsiteHealthRecommendation(
            $"{problem} {sourceExplanation} Review the result for accuracy before publishing.",
            BuildTailoredDescription(
                url,
                GetPageTopic(url, heading, title),
                introductoryText,
                currentDescription),
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url),
            NormalizeText(currentDescription));
    }

    internal static WebsiteHealthRecommendation DuplicateMetaDescription(
        Uri url,
        string? title,
        string? heading,
        string? introductoryText,
        Uri matchingUrl)
    {
        return new WebsiteHealthRecommendation(
            $"This description also appears on {matchingUrl.PathAndQuery}. Replace it with copy that describes only this page and gives customers a specific reason to visit it.",
            BuildDescription(
                url,
                GetPageTopic(url, heading, title),
                introductoryText),
            GetShopifySeoLocation(url),
            GetShopifySeoDocumentation(url));
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

    internal static WebsiteHealthRecommendation CanonicalQuality(
        Uri url,
        string currentCanonical)
    {
        var canonical = url.GetLeftPart(UriPartial.Path).TrimEnd('/');
        if (url.AbsolutePath == "/")
        {
            canonical += "/";
        }

        return new WebsiteHealthRecommendation(
            "Replace the current canonical with the clean production URL shown below. Shopify normally generates this from canonical_url; remove hard-coded domain, tracking, or cross-page overrides that replace it.",
            $"""<link rel="canonical" href="{WebUtility.HtmlEncode(canonical)}">""",
            "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head>; search for canonical_url and rel=\"canonical\"",
            "https://shopify.dev/docs/storefronts/themes/seo/metadata",
            currentCanonical);
    }

    internal static WebsiteHealthRecommendation SearchIndexability(
        Uri url,
        string? currentDirective)
    {
        return new WebsiteHealthRecommendation(
            "This page appears to be a public product, collection, article, or content page. Confirm it is active and published to the Online Store, then remove the noindex rule applying to it. A public Shopify page can use index,follow or omit the robots meta tag entirely.",
            """<meta name="robots" content="index,follow">""",
            GetShopifyIndexabilityLocation(url),
            "https://help.shopify.com/en/manual/promoting-marketing/seo/hide-a-page-from-search-engines",
            currentDirective);
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

    internal static WebsiteHealthRecommendation SchemaQuality(
        Uri url,
        int invalidBlockCount,
        string? missingExpectedType,
        IReadOnlyList<string>? propertyProblems = null)
    {
        propertyProblems ??= [];
        var problems = new List<string>();
        if (invalidBlockCount > 0)
        {
            problems.Add(
                $"repair or remove {invalidBlockCount} malformed JSON-LD block(s)");
        }

        if (missingExpectedType is not null)
        {
            problems.Add($"restore the {missingExpectedType} schema for this page");
        }

        if (propertyProblems.Count > 0)
        {
            problems.Add(
                $"restore these live-data fields: {string.Join(", ", propertyProblems)}");
        }

        var expectedGuidance =
            missingExpectedType == "Product" ||
            (propertyProblems.Count > 0 &&
                url.AbsolutePath.StartsWith(
                    "/products/",
                    StringComparison.OrdinalIgnoreCase))
            ? "Product markup should use Shopify's live product data for name, canonical URL, image, brand, offers, price, currency, and availability."
            : missingExpectedType switch
            {
                "Article" =>
                    "Article markup should identify the headline, canonical URL, image, publication date, author, and publisher.",
                "WebSite" =>
                    "WebSite markup should identify the store name and canonical homepage URL; include SearchAction only when its target matches the live search route.",
                _ =>
                    "Keep the markup aligned with the visible page and its canonical URL."
            };
        return new WebsiteHealthRecommendation(
            $"Structured data must be valid JSON and describe the page customers can see. {string.Join(" and ", problems)}. {expectedGuidance} Do not paste a static price or availability value that can drift from Shopify.",
            missingExpectedType is null
                ? propertyProblems.Count > 0
                    ? $"Product structured data needs:\n{string.Join(
                        Environment.NewLine,
                        propertyProblems.Select(problem => $"- {problem}"))}"
                    : "Validate the affected JSON-LD block, correct its JSON syntax, and rerun Website Health."
                : $"Expected schema type: {missingExpectedType}",
            GetShopifySchemaLocation(url),
            "https://developers.google.com/search/docs/appearance/structured-data/intro-structured-data");
    }

    internal static WebsiteHealthRecommendation SocialPreview(
        Uri url,
        string? title,
        string? heading,
        string? introductoryText,
        string? metaDescription,
        string? openGraphTitle,
        string? openGraphDescription,
        string? openGraphImage,
        string? openGraphUrl,
        string? twitterCard)
    {
        var topic = GetPageTopic(url, heading, title);
        var suggestedTitle = string.IsNullOrWhiteSpace(title)
            ? BuildTitle(url, heading)
            : NormalizeText(title);
        var suggestedDescription =
            string.IsNullOrWhiteSpace(metaDescription)
                ? BuildDescription(url, topic, introductoryText)
                : FitSearchDescription(metaDescription);
        var canonicalUrl = url.GetLeftPart(UriPartial.Path);
        var suggestedImage = string.IsNullOrWhiteSpace(openGraphImage)
            ? "HTTPS_URL_FOR_THIS_PAGE_IMAGE"
            : openGraphImage;
        var suggestedValue =
            $"""
            <meta property="og:title" content="{WebUtility.HtmlEncode(suggestedTitle)}">
            <meta property="og:description" content="{WebUtility.HtmlEncode(suggestedDescription)}">
            <meta property="og:image" content="{WebUtility.HtmlEncode(suggestedImage)}">
            <meta property="og:url" content="{WebUtility.HtmlEncode(canonicalUrl)}">
            <meta name="twitter:card" content="summary_large_image">
            """;
        var currentValue =
            $"""
            og:title: {DisplayMetadataValue(openGraphTitle)}
            og:description: {DisplayMetadataValue(openGraphDescription)}
            og:image: {DisplayMetadataValue(openGraphImage)}
            og:url: {DisplayMetadataValue(openGraphUrl)}
            twitter:card: {DisplayMetadataValue(twitterCard)}
            """;

        return new WebsiteHealthRecommendation(
            "Complete the social preview with this page's current title, description, canonical URL, and a relevant landscape image. Use the product's featured image on product pages and the collection or page image elsewhere. The suggested text is tailored from the live page; replace the image placeholder with Shopify's dynamic image output rather than a hard-coded URL.",
            suggestedValue,
            GetShopifySocialPreviewLocation(url),
            "https://shopify.dev/docs/storefronts/themes/seo/metadata",
            currentValue);
    }

    private static string GetShopifySocialPreviewLocation(Uri url)
    {
        var contentLocation = url.AbsolutePath.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase)
            ? "Products → open this product → confirm its title, description, SEO listing, and featured media"
            : url.AbsolutePath.StartsWith(
                "/collections/",
                StringComparison.OrdinalIgnoreCase)
                ? "Products → Collections → open this collection → confirm its title, description, SEO listing, and collection image"
                : url.AbsolutePath.StartsWith(
                    "/blogs/",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Content → Blog posts → open the matching article or Manage blogs for the blog index → confirm its SEO listing and image"
                    : url.AbsolutePath == "/"
                        ? "Online Store → Themes → Customize → Home page → Social media or sharing image settings"
                        : "Online Store → Pages → open this page → confirm its title, content, SEO listing, and image";

        return $"Shopify Admin → {contentLocation}. If the live source still lacks the tags, use Online Store → Themes → … → Edit code → search the entire theme for og:title, social-meta-tags, or meta-tags; inspect the matching snippet rendered inside <head>.";
    }

    private static string DisplayMetadataValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "(missing)"
            : NormalizeText(value);

    private static string GetShopifySchemaLocation(Uri url)
    {
        var template = url.AbsolutePath.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase)
            ? "the product template"
            : url.AbsolutePath.StartsWith(
                "/blogs/",
                StringComparison.OrdinalIgnoreCase)
                ? "the affected blog/article template"
                : url.AbsolutePath == "/"
                    ? "the home page template"
                    : "the affected page template";
        return $"Shopify Admin → Online Store → Themes → … → Edit code → search the entire theme for application/ld+json or the missing schema type → inspect the snippet rendered by {template}. Also review active app embeds that inject SEO markup.";
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
                return new ImageSuggestion(
                    $"{sourceLabel}: alt=\"{altText}\"",
                    new WebsiteHealthImageEvidence(
                        ResolveImageUrl(image.Source, image.PageUrl),
                        image.PageUrl,
                        null,
                        altText,
                        GetSuggestionSource(image.Context, image.Source)));
            })
            .ToList();
        var suggestions = generatedSuggestions
            .Select(item => item.DisplayValue)
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
            GetShopifyImageAltLocation(pageUrl),
            "https://help.shopify.com/en/manual/products/product-media/add-alt-text",
            EvidenceJson: SerializeImageEvidence(
                generatedSuggestions
                    .Select(item => item.Evidence)
                    .OfType<WebsiteHealthImageEvidence>()));
    }

    internal static WebsiteHealthRecommendation ImageAltQuality(
        Uri pageUrl,
        string? title,
        string? heading,
        string currentAltText,
        IReadOnlyList<WebsiteHealthImage> images,
        bool isReusedAcrossAssets)
    {
        var topic = GetPageTopic(pageUrl, heading, title);
        var uniqueImages = images
            .GroupBy(
                image => image.Source ?? image.PageUrl ?? "",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();
        var suggestions = uniqueImages
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
                return new ImageSuggestion(
                    $"{sourceLabel}: alt=\"{altText}\"",
                    new WebsiteHealthImageEvidence(
                        ResolveImageUrl(image.Source, image.PageUrl),
                        image.PageUrl,
                        image.AltText,
                        altText,
                        GetSuggestionSource(image.Context, image.Source)));
            })
            .ToList();
        if (images.Count > uniqueImages.Count)
        {
            suggestions.Add(new ImageSuggestion(
                $"+ {images.Count - uniqueImages.Count} more image(s) to review",
                null));
        }

        var guidance = isReusedAcrossAssets
            ? "This same alt text is attached to different image files. Keep identical alt text only when the images communicate the same thing. Otherwise, describe each image’s specific product, color, material, or purpose; decorative images should use alt=\"\"."
            : "This alt text is too generic or looks like a filename, so it does not explain the image to customers using assistive technology. Replace it with a short description of the useful visual information, or use alt=\"\" when the image is decorative.";
        return new WebsiteHealthRecommendation(
            guidance,
            string.Join(
                Environment.NewLine,
                suggestions.Select(item => item.DisplayValue)),
            GetShopifyImageAltLocation(pageUrl),
            "https://help.shopify.com/en/manual/products/product-media/add-alt-text",
            $"Current alt text: \"{NormalizeText(currentAltText)}\"",
            SerializeImageEvidence(
                suggestions
                    .Select(item => item.Evidence)
                    .OfType<WebsiteHealthImageEvidence>()));
    }

    private static string SerializeImageEvidence(
        IEnumerable<WebsiteHealthImageEvidence> evidence) =>
        JsonSerializer.Serialize(evidence);

    private static string GetSuggestionSource(
        string? context,
        string? source)
    {
        var normalizedContext = NormalizeText(context);
        if (normalizedContext.Length is >= 3 and <= 100 &&
            !LooksGeneric(normalizedContext))
        {
            return "Nearby page context";
        }

        var filename = GetFilenameTopic(source);
        return !string.IsNullOrWhiteSpace(filename) &&
            !LooksGeneric(filename)
                ? "Descriptive filename"
                : "Page topic fallback";
    }

    private static string? ResolveImageUrl(
        string? source,
        string? pageUrl)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var normalizedSource = source.StartsWith(
            "//",
            StringComparison.Ordinal)
            ? $"https:{source}"
            : source;
        Uri? resolved = null;
        if (Uri.TryCreate(
            normalizedSource,
            UriKind.Absolute,
            out var absolute))
        {
            resolved = absolute;
        }
        else if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) &&
            Uri.TryCreate(page, normalizedSource, out var relative))
        {
            resolved = relative;
        }

        if (resolved is null ||
            resolved.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return resolved.ToString();
    }

    private sealed record ImageSuggestion(
        string DisplayValue,
        WebsiteHealthImageEvidence? Evidence);

    private static string GetShopifyImageAltLocation(Uri pageUrl)
    {
        var path = pageUrl.AbsolutePath;
        if (path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Products → open the product matching this URL → Media → select the affected image → Edit alt text";
        }

        if (path.StartsWith(
            "/collections/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → if this is a product-card image, Products → open the product shown → Media → select the image → Edit alt text; if it is the collection banner, Products → Collections → open this collection → edit the collection image";
        }

        if (path.StartsWith(
            "/pages/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Online Store → Pages → open the page matching this URL → edit the content image; if the image comes from a theme section, Online Store → Themes → Customize → open this page template → select the image section";
        }

        if (path is "" or "/")
        {
            return "Shopify Admin → Online Store → Themes → Customize → Home page → open the Slideshow or Collection tabs section → select the affected slide/block and its image. If the section has no alt-text field, open Content → Files → search for the filename shown in GHOS → open the image → edit its alt text";
        }

        return "Shopify Admin → Online Store → Themes → Customize → open the affected page/template → select the section containing this image; for product media, use Products → open the product → Media → select the image → Edit alt text";
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

    internal static WebsiteHealthRecommendation BrokenImage(
        Uri source,
        IReadOnlyList<Uri> affectedPages,
        int? statusCode)
    {
        var status = statusCode is null
            ? "did not respond"
            : $"returned HTTP {statusCode}";
        var pageList = string.Join(
            Environment.NewLine,
            affectedPages
                .Take(8)
                .Select(page => page.ToString()));
        var location = affectedPages.Count > 0
            ? GetShopifyImageAltLocation(affectedPages[0])
            : "Shopify Admin → Content → Files and Online Store → Themes → Customize";
        return new WebsiteHealthRecommendation(
            $"This image {status}. Confirm that the source file still exists and is published, then replace the affected image reference with a working HTTPS asset. If the image was intentionally removed, remove its empty image block instead of leaving a broken customer-facing placeholder.",
            source.ToString(),
            location,
            CurrentValue:
                $"Affected crawled page(s):\n{pageList}");
    }

    internal static WebsiteHealthRecommendation RedirectChain(
        Uri requestedUrl,
        Uri finalUrl,
        int redirectCount)
    {
        return new WebsiteHealthRecommendation(
            $"Replace links to the starting URL with the final HTTPS URL so customers and search engines do not traverse {redirectCount} redirects. Keep the final redirect in place for old bookmarks and external links, but remove intermediate redirects after confirming they are no longer needed.",
            finalUrl.ToString(),
            "Shopify Admin → Online Store → Navigation, Content → Pages/Blog posts, and Online Store → Themes → Customize → find links using the starting URL; also review Online Store → Navigation → URL redirects",
            "https://help.shopify.com/en/manual/online-store/menus-and-links/url-redirect",
            requestedUrl.ToString());
    }

    internal static WebsiteHealthRecommendation SecurityHeaders(
        Uri pageUrl,
        IReadOnlyList<string> missingHeaders)
    {
        return new WebsiteHealthRecommendation(
            "These headers are delivered by Shopify and the storefront CDN, not by page content or a Liquid metadata snippet. Confirm the primary domain has a fully provisioned TLS certificate and that Cloudflare is not stripping or replacing Shopify response headers. Review proxy/transform rules and app proxies, then contact Shopify Support if the headers are still absent at the origin. Do not paste header text into theme.liquid.",
            $"Expected protections:\n{string.Join(
                Environment.NewLine,
                missingHeaders.Select(header => $"- {header}"))}",
            "Shopify Admin → Settings → Domains → open the primary domain and confirm TLS status; Cloudflare → Rules/Transform Rules and SSL/TLS → review response-header changes for this hostname",
            "https://help.shopify.com/en/manual/domains/managing-domains/secure-connections",
            pageUrl.ToString());
    }

    internal static WebsiteHealthRecommendation RobotsQuality(
        Uri baseUri,
        bool blocksStorefront,
        bool hasProductionSitemap)
    {
        var actions = new List<string>();
        if (blocksStorefront)
        {
            actions.Add(
                "remove the User-agent: * rule that outputs Disallow: /");
        }

        if (!hasProductionSitemap)
        {
            actions.Add(
                $"restore the sitemap declaration for {new Uri(baseUri, "/sitemap.xml")}");
        }

        return new WebsiteHealthRecommendation(
            $"Keep Shopify's default crawler protections, but {string.Join(" and ", actions)}. Do not replace the entire file with a blanket Allow rule because Shopify's defaults protect checkout, account, cart, and filtered URLs.",
            $"Required result:\nPublic storefront pages remain crawlable.\nSitemap: {new Uri(baseUri, "/sitemap.xml")}",
            "Shopify Admin → Online Store → Themes → … → Edit code → templates/robots.txt.liquid; compare custom rules with robots.default_groups",
            "https://help.shopify.com/en/manual/promoting-marketing/seo/editing-robots-txt");
    }

    internal static WebsiteHealthRecommendation SitemapQuality(
        Uri sitemapUri,
        SitemapAnalysis analysis)
    {
        var problem = !analysis.IsValidXml
            ? "The response is not valid XML."
            : !analysis.HasSupportedRoot
                ? "The document is not a sitemap index or URL set."
                : analysis.LocationCount == 0
                    ? "The document contains no locations."
                    : "One or more locations use an invalid URL or another domain.";
        return new WebsiteHealthRecommendation(
            $"{problem} Shopify generates sitemap.xml automatically, so do not create or paste a static sitemap file into the theme. Confirm the affected products, collections, pages, and articles are published to the Online Store. Then review domain forwarding or CDN/proxy rules that may be rewriting the response; contact Shopify Support if the generated route remains invalid.",
            sitemapUri.ToString(),
            "Shopify Admin → Products and Content → confirm public items are published to Online Store; Settings → Domains → confirm the primary domain and forwarding. sitemap.xml itself is generated by Shopify.",
            "https://help.shopify.com/en/manual/promoting-marketing/seo/find-site-map");
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
        else if (path.Equals(
            "/pages/b2b-portal",
            StringComparison.OrdinalIgnoreCase))
        {
            description =
                "Apply for a Green Hills Supply contractor house account with business billing, payment terms, and tax-exemption options for approved customers.";
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

    private static string BuildTailoredDescription(
        Uri url,
        string topic,
        string? introductoryText,
        string? currentDescription)
    {
        var current = NormalizeText(currentDescription);
        if (current.Length >= 70)
        {
            return FitSearchDescription(
                current,
                url.AbsolutePath.StartsWith(
                    "/products/",
                    StringComparison.OrdinalIgnoreCase));
        }

        var fallback = BuildDescription(url, topic, introductoryText);
        if (current.Length < 20)
        {
            return fallback;
        }

        var combined =
            $"{current.TrimEnd(' ', '.', '!', '?')}—{fallback}";
        return FitSearchDescription(combined);
    }

    private static string FitSearchDescription(
        string value,
        bool preferLeadSentence = false)
    {
        var sentences = Regex.Split(
                NormalizeText(value),
                @"(?<=[.!?])\s+(?=[A-Z0-9])")
            .Select(NormalizeText)
            .Where(sentence => sentence.Length >= 40)
            .ToList();
        var leadSentence = sentences.FirstOrDefault();
        if (preferLeadSentence &&
            !string.IsNullOrWhiteSpace(leadSentence))
        {
            var leadCandidates = new[]
                {
                    CompressDescriptionSentence(leadSentence),
                    leadSentence
                }
                .Select(EnsureSentence)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(sentence => sentence.Length is >= 70 and <= 155)
                .ToList();
            if (leadCandidates.Count > 0)
            {
                return leadCandidates[0];
            }

            if (leadSentence.Length > 155)
            {
                var fittedLead = TruncateAtWord(
                        CompressDescriptionSentence(leadSentence),
                        151)
                    .TrimEnd(
                        ' ',
                        ',',
                        ';',
                        ':',
                        '-',
                        '—',
                        '.',
                        '!',
                        '?');
                return $"{fittedLead}…";
            }
        }

        var candidates = (preferLeadSentence
                ? sentences.Skip(1)
                : sentences)
            .SelectMany(sentence => new[]
            {
                sentence,
                CompressDescriptionSentence(sentence)
            })
            .Select(EnsureSentence)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(sentence => sentence.Length is >= 70 and <= 155)
            .OrderByDescending(sentence => sentence.Length)
            .ToList();
        if (candidates.Count > 0)
        {
            return candidates[0];
        }

        var compressed = CompressDescriptionSentence(
            sentences.FirstOrDefault() ?? value);
        if (compressed.Length is >= 70 and <= 155)
        {
            return EnsureSentence(compressed);
        }

        var fitted = TruncateAtWord(compressed, 151)
            .TrimEnd(' ', ',', ';', ':', '-', '—', '.', '!', '?');
        return $"{fitted}…";
    }

    private static string CompressDescriptionSentence(string value)
    {
        var compressed = NormalizeText(value);
        compressed = Regex.Replace(
            compressed,
            @"^From\s+(.+?),\s+our\s+[^,]{1,60}\s+(?:are|is)\s+ideal\s+for\s+",
            "$1 support ",
            RegexOptions.IgnoreCase);
        compressed = Regex.Replace(
            compressed,
            @"^Our\s+",
            "",
            RegexOptions.IgnoreCase);
        compressed = compressed.Replace(
            " and gravel to base and ",
            ", gravel, base and ",
            StringComparison.OrdinalIgnoreCase);
        var replacements = new[]
        {
            ("inlarger", "in larger"),
            ("usedalone", "used alone"),
            ("withcompatible", "with compatible"),
            (
                " is a pelletized seed starter made from ",
                " uses "),
            (
                " is a convenient 2.5 cu. ft. bag designed to protect ",
                " protects "),
            (" to help protect ", " to protect "),
            (
                ", retain moisture, and help control ",
                ", retains moisture and helps control "),
            (
                "support faster germination",
                "support germination"),
            (" selection includes ", " offers "),
            (" products include a full range of ", " offers "),
            (" make it easy to get ", " provide "),
            (" offer an effective way to improve ", " improve "),
            (" are perfect for ", " suit "),
            (" are ideal for ", " support "),
            (" you need for ", " for "),
            (", and other products", " and more"),
            (" throughout the winter season", " all winter")
        };
        foreach (var replacement in replacements)
        {
            compressed = compressed.Replace(
                replacement.Item1,
                replacement.Item2,
                StringComparison.OrdinalIgnoreCase);
        }

        compressed = compressed.Replace(", and ", " and ");
        if (compressed.Length > 0)
        {
            compressed =
                char.ToUpperInvariant(compressed[0]) + compressed[1..];
        }

        return compressed;
    }

    private static string BuildTitle(Uri url, string? heading)
    {
        if (url.AbsolutePath == "/")
        {
            return "Green Hills Supply | Landscape & Outdoor Materials";
        }

        var topic = CondenseTitleTopic(
            !string.IsNullOrWhiteSpace(heading)
                ? heading
                : GetPageTopic(url, null, null));
        var branded = $"{topic} | {BrandName}";
        return branded.Length <= 60
            ? branded
            : topic;
    }

    private static string BuildTailoredTitle(
        Uri url,
        string? heading,
        string currentTitle)
    {
        var normalizedTitle = NormalizeText(currentTitle);
        var segments = Regex
            .Split(normalizedTitle, @"\s*(?:\||[–—])\s*")
            .Select(segment => NormalizeText(segment.Replace(
                BrandName,
                "",
                StringComparison.OrdinalIgnoreCase)))
            .Where(segment => segment.Length > 0)
            .ToList();
        var headingTopic = !string.IsNullOrWhiteSpace(heading)
            ? CondenseTitleTopic(heading)
            : null;
        var currentTopic = segments.FirstOrDefault();
        var topic = BuildTailoredTitleTopic(
            headingTopic,
            currentTopic,
            GetPageTopic(url, null, null));
        var descriptor = segments
            .Skip(1)
            .FirstOrDefault(segment =>
                !segment.Equals(topic, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(descriptor))
        {
            return BuildTitle(url, topic);
        }

        descriptor = CompactTitleDescriptor(topic, descriptor);
        var tailored = TrimDanglingTitleWords(
            TruncateAtWord($"{topic} | {descriptor}", 60));
        var branded = $"{tailored} | {BrandName}";
        return branded.Length <= 60
            ? branded
            : tailored;
    }

    private static string CondenseTitleTopic(string value)
    {
        var topic = NormalizeText(value);
        if (topic.Length <= 60)
        {
            return topic;
        }

        var withoutParenthetical = Regex.Replace(
            topic,
            @"\s*\([^)]*\)\s*$",
            "").Trim();
        if (withoutParenthetical.Length is >= 20 and <= 60)
        {
            return withoutParenthetical;
        }

        var clause = topic
            .Split(
                [':', '–', '—'],
                2,
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (clause?.Length is >= 20 and <= 60)
        {
            return clause;
        }

        return TrimDanglingTitleWords(
            TruncateAtWord(topic, 60))
            .TrimEnd('(', '[', '{');
    }

    private static string BuildTailoredTitleTopic(
        string? headingTopic,
        string? currentTopic,
        string fallbackTopic)
    {
        if (string.IsNullOrWhiteSpace(headingTopic))
        {
            return currentTopic ?? fallbackTopic;
        }

        if (!string.IsNullOrWhiteSpace(currentTopic) &&
            currentTopic.StartsWith(
                $"{headingTopic} ",
                StringComparison.OrdinalIgnoreCase))
        {
            var suffix = currentTopic[(headingTopic.Length + 1)..]
                .ToLowerInvariant();
            if (suffix is "stone" or "mulch" or "sand" or "soil" or
                "gravel" or "base")
            {
                return currentTopic;
            }
        }

        return headingTopic;
    }

    private static string CompactTitleDescriptor(
        string topic,
        string descriptor)
    {
        var compact = NormalizeText(descriptor);
        var replacements = new[]
        {
            ("Paths & Light-Duty Projects", "Paths & Light Projects"),
            ("Bed & Landscape Project", "Beds & Landscaping"),
            ("Garden & Landscape Beds", "Gardens & Beds"),
            ("Landscape Beds & Gardens", "Beds & Gardens"),
            ("Beds, Trees & Landscape", "Beds & Trees"),
            ("Lawns, Garden & Landscaping", "Lawns & Gardens"),
            ("Decorative Drainage Stone", "Drainage Stone"),
            ("Decorative Landscape Rock", "Landscape Rock"),
            ("Premium Colored Mulch", "Colored Mulch"),
            ("Premium Soil Blend", "Soil Blend"),
            ("Concrete & Construction", "Concrete Work"),
            ("Landscape Features", "Landscaping"),
            ("Landscape Bed & Garden", "Beds & Gardens"),
            ("Landscape Bed and Garden", "Beds & Gardens"),
            ("Landscape Project", "Landscaping"),
            ("Light-Duty Projects", "Paths"),
            ("Construction Project", "Construction"),
            ("Outdoor Spaces", "Outdoor Areas")
        };
        foreach (var replacement in replacements)
        {
            compact = compact.Replace(
                replacement.Item1,
                replacement.Item2,
                StringComparison.OrdinalIgnoreCase);
        }

        if (topic.Contains("mulch", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact
                .Replace(
                    "Triple-Ground Mulch",
                    "Triple-Ground",
                    StringComparison.OrdinalIgnoreCase)
                .Replace(
                    "Premium Brown Mulch",
                    "Premium Brown",
                    StringComparison.OrdinalIgnoreCase);

            if (topic.Contains(
                "cedar",
                StringComparison.OrdinalIgnoreCase))
            {
                compact = compact.Replace(
                    "100% Cedar Mulch",
                    "100% Cedar",
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        if (topic.Contains("base", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact.Replace(
                "Limestone Base",
                "Limestone",
                StringComparison.OrdinalIgnoreCase);
        }

        if (topic.Contains("topsoil", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact.Replace(
                "Premium Topsoil for ",
                "",
                StringComparison.OrdinalIgnoreCase);
        }

        return compact;
    }

    private static string TrimDanglingTitleWords(string title)
    {
        var trimmed = title;
        while (Regex.IsMatch(
            trimmed,
            @"(?:\s+(?:and|for|of|the|with|to|in)|\s*&|\s*[|–—])$",
            RegexOptions.IgnoreCase))
        {
            trimmed = Regex.Replace(
                trimmed,
                @"(?:\s+(?:and|for|of|the|with|to|in)|\s*&|\s*[|–—])$",
                "",
                RegexOptions.IgnoreCase).TrimEnd();
        }

        return trimmed;
    }

    private static string GetShopifyHeadingLocation(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.Equals(
            "/pages/accessibility",
            StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Online Store → Themes → Customize → use the top page selector to open Pages → Accessibility → add a Custom liquid section immediately above the Rich text section → paste the suggested H1 → clear “Accessibility Statement” from the existing Rich text Heading field so the title appears once";
        }

        var contentArea = path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase)
            ? "the product template"
            : path.StartsWith(
                "/collections/",
                StringComparison.OrdinalIgnoreCase)
                ? "the collection template"
                : path.StartsWith(
                    "/blogs/",
                    StringComparison.OrdinalIgnoreCase)
                    ? "the blog or article template"
                    : path.StartsWith(
                        "/pages/",
                        StringComparison.OrdinalIgnoreCase)
                        ? "the page template"
                        : "the home page template";
        return $"Shopify Admin → Online Store → Themes → Customize → open {contentArea} → review heading sections; use Edit code only if the duplicate or missing H1 comes from the template";
    }

    private static string GetShopifyIndexabilityLocation(Uri url)
    {
        var path = url.AbsolutePath;
        var resource = path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase)
            ? "Products → open this product → confirm Active and Online Store publishing"
            : path.StartsWith(
                "/collections/",
                StringComparison.OrdinalIgnoreCase)
                ? "Products → Collections → open this collection → confirm Online Store availability"
                : path.StartsWith(
                    "/blogs/",
                    StringComparison.OrdinalIgnoreCase)
                    ? "Content → Blog posts → open this article → confirm it is visible"
                    : path.StartsWith(
                        "/pages/",
                        StringComparison.OrdinalIgnoreCase)
                        ? "Online Store → Pages → open this page → confirm it is visible"
                        : "Online Store → Themes → open the active theme";
        return $"Shopify Admin → {resource}; if it is published, use Online Store → Themes → … → Edit code and search for noindex";
    }

    private static string GetPageTopic(
        Uri url,
        string? heading,
        string? title)
    {
        var candidate = SelectPageTopicSource(url, heading, title);
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

    private static string? SelectPageTopicSource(
        Uri url,
        string? heading,
        string? title)
    {
        var normalizedHeading = NormalizeText(heading);
        var normalizedTitle = NormalizeText(title);
        if (normalizedHeading.Length >= 3 &&
            normalizedTitle.Length >= 3 &&
            MatchesUrlTopic(url, normalizedTitle) &&
            !MatchesUrlTopic(url, normalizedHeading))
        {
            return normalizedTitle;
        }

        return FirstMeaningful(normalizedHeading, normalizedTitle);
    }

    private static bool MatchesUrlTopic(Uri url, string value)
    {
        var normalizedValue = NormalizeText(value)
            .ToLowerInvariant();
        var slug = url.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? "";
        var slugTerms = Regex
            .Split(WebUtility.UrlDecode(slug).ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(term =>
                term.Length >= 3 &&
                term is not ("page" or "portal" or "green" or "hills" or
                    "supply"))
            .ToList();
        return slugTerms.Count > 0 &&
            slugTerms.Any(normalizedValue.Contains);
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
        var decoded = Regex.Replace(
            WebUtility.UrlDecode(value),
            @"(?<=[a-z])(?=[A-Z])",
            " ");
        foreach (var character in decoded)
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

    private static string NormalizeText(string? value)
    {
        var normalized = Regex.Replace(
            WebUtility.HtmlDecode(value ?? ""),
            @"\s+",
            " ").Trim();
        return Regex.Replace(
            normalized,
            @"(?<=[.!?])(?=[A-Z])",
            " ");
    }

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
            return "Shopify Admin → Products → Collections → search for All. If an existing collection's Search engine listing has the handle all, leave its products unchanged; choose Edit website SEO and paste the suggested description. If no editable All collection exists, choose Add collection → Add title → enter All. In the default Products source, choose Add condition (not Add products) → Product price → is greater than → $0. Then edit the Search engine listing → keep the handle exactly all → paste the suggested description → Save. The new Shopify Collections experience intentionally has no Smart or Manual type selector; conditions now provide automatic membership. Leave your existing collections landing page, homepage sections, theme template, and menu links unchanged.";
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

        var segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 &&
            segments[0].Equals(
                "blogs",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Content → Blog posts → Manage blogs → open this blog → Search engine listing preview → pencil icon";
        }

        if (segments.Length >= 3 &&
            segments[0].Equals(
                "blogs",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Content → Blog posts → open this post → Search engine listing → Edit";
        }

        if (path.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase))
        {
            return "Shopify Admin → Online Store → Pages → open this page → Search engine listing → Edit website SEO";
        }

        return "Shopify Admin → Online Store → Themes → … → Edit code → layout/theme.liquid → inside <head>";
    }

    internal static string? GetShopifySeoDocumentation(Uri url)
    {
        var path = url.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            return "https://help.shopify.com/en/manual/promoting-marketing/seo/adding-keywords";
        }

        if (path.Equals(
            "/collections/all",
            StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/products/collections/create-collection";
        }

        if (path.StartsWith(
            "/collections/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/products/collections/make-collections-available";
        }

        if (path.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/products/add-update-products";
        }

        var segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 &&
            segments[0].Equals(
                "blogs",
                StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/online-store/blogs/adding-a-blog";
        }

        if (segments.Length >= 3 &&
            segments[0].Equals(
                "blogs",
                StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/online-store/blogs/writing-blogs/working-with-blog-posts";
        }

        if (path.StartsWith("/pages/", StringComparison.OrdinalIgnoreCase))
        {
            return "https://help.shopify.com/en/manual/online-store/add-edit-pages";
        }

        return null;
    }

    private static bool LooksGeneric(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized is "image" or "photo" or "picture" or "thumbnail" ||
            normalized is "gallery viewer" or "featured collections" ||
            normalized.Contains("untitled design", StringComparison.Ordinal) ||
            normalized.Contains("blank logo", StringComparison.Ordinal) ||
            normalized.StartsWith("istock", StringComparison.Ordinal) ||
            normalized.StartsWith("i stock", StringComparison.Ordinal) ||
            normalized.StartsWith("depositphotos", StringComparison.Ordinal) ||
            normalized.StartsWith("shutterstock", StringComparison.Ordinal) ||
            normalized.StartsWith("getty", StringComparison.Ordinal) ||
            normalized.StartsWith("adobe stock", StringComparison.Ordinal) ||
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
