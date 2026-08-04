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
            $"{problem} Replace it with a unique title near 50–60 characters that leads with the page topic and ends with the Green Hills Supply brand.",
            BuildTitle(url, heading),
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

        var topic = GetPageTopic(url, heading, title);
        var description = BuildDescription(
            url,
            topic,
            introductoryText);
        var guidance = url.AbsolutePath.Equals(
            "/collections/all",
            StringComparison.OrdinalIgnoreCase)
            ? "This is Shopify's built-in catalog route. Do not rename or change the handle of an existing collection, because theme sections can depend on that handle. To manage this route's SEO, create a separate smart collection titled All with the handle /all, then add this description. If the catalog route is intentionally unused, suppress this finding or remove it from Website Health key pages."
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

        Uri? resolved = null;
        if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
        {
            resolved = absolute;
        }
        else if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var page) &&
            Uri.TryCreate(page, source, out var relative))
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

    private static string BuildTailoredDescription(
        Uri url,
        string topic,
        string? introductoryText,
        string? currentDescription)
    {
        var current = NormalizeText(currentDescription);
        if (current.Length >= 70)
        {
            return FitSearchDescription(current);
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

    private static string FitSearchDescription(string value)
    {
        var sentences = Regex.Matches(
                NormalizeText(value),
                @"[^.!?]+[.!?]")
            .Select(match => NormalizeText(match.Value))
            .Where(sentence => sentence.Length >= 40)
            .ToList();
        var candidates = sentences
            .Concat(sentences.Select(CompressDescriptionSentence))
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
        var topic = GetPageTopic(url, heading, null);
        var title = url.AbsolutePath == "/"
            ? "Green Hills Supply | Landscape & Outdoor Materials"
            : $"{topic} | {BrandName}";
        return TruncateAtWord(title, 60);
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
        Regex.Replace(
            WebUtility.HtmlDecode(value ?? ""),
            @"\s+",
            " ").Trim();

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
            return "Shopify Admin → Products → Collections → Create collection → create a separate smart collection titled All → Save → Search engine listing → Edit website SEO → confirm the new collection handle is /all. Do not rename an existing collection.";
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
            return "https://help.shopify.com/en/manual/online-store/themes/customizing-themes/common-customizations/change-catalog-page";
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
            normalized.Contains("untitled design", StringComparison.Ordinal) ||
            normalized.Contains("blank logo", StringComparison.Ordinal) ||
            normalized.StartsWith("istock", StringComparison.Ordinal) ||
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
