using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.WebsiteHealth;

internal sealed record StructuredDataAnalysis(
    int BlockCount,
    int ValidBlockCount,
    int InvalidBlockCount,
    IReadOnlySet<string> SchemaTypes,
    ProductStructuredDataAnalysis Product);

internal sealed record ProductStructuredDataAnalysis(
    bool HasName,
    bool HasImage,
    bool HasOffers,
    bool HasPrice,
    bool HasPriceCurrency,
    bool HasAvailability,
    IReadOnlyList<string> ProductUrls);

internal sealed record SitemapAnalysis(
    bool IsValidXml,
    bool HasSupportedRoot,
    int LocationCount,
    int InvalidLocationCount,
    int ExternalLocationCount,
    string? Error);

internal sealed record SecurityHeaderAnalysis(
    bool HasStrictTransportSecurity,
    bool HasContentTypeProtection,
    bool HasFramingProtection,
    bool HasContentSecurityPolicy,
    IReadOnlyList<string> MissingHeaders)
{
    internal bool IsHealthy => MissingHeaders.Count == 0;
}

public sealed class WebsiteHealthMonitorService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    HttpClient httpClient,
    ILogger<WebsiteHealthMonitorService> logger)
{
    private const int MaximumResponseCharacters = 2_000_000;
    private readonly HtmlParser _htmlParser = new();

    public async Task<WebsiteCheckRun> RunAsync(
        Guid siteId,
        string trigger,
        string? requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var site = await dbContext.MonitoredSites
            .Include(item => item.Checks)
            .SingleAsync(item => item.Id == siteId, cancellationToken);

        var run = new WebsiteCheckRun
        {
            MonitoredSiteId = site.Id,
            Trigger = trigger,
            RequestedByUserId = requestedByUserId
        };
        dbContext.WebsiteCheckRuns.Add(run);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var baseUri = await ValidateTargetAsync(
                site.BaseUrl,
                cancellationToken);
            var observations = new List<Observation>();
            var issues = new List<DetectedIssue>();
            var enabledCheckKeys = site.Checks
                .Where(check => check.IsEnabled)
                .Select(check => check.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var pages = new Dictionary<string, PageSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            var checkedUrls = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            if (enabledCheckKeys.Contains("ssl"))
            {
                await CheckSslAsync(
                    baseUri,
                    site,
                    observations,
                    issues,
                    cancellationToken);
            }

            var homepage = await FetchPageAsync(
                baseUri,
                site,
                cancellationToken);
            checkedUrls.Add(NormalizeUrl(baseUri));
            AddAvailabilityObservation(
                "homepage",
                "Homepage availability",
                baseUri,
                homepage,
                observations,
                issues,
                critical: true);
            if (enabledCheckKeys.Contains("redirect-chain"))
            {
                AddRedirectObservation(
                    baseUri,
                    homepage,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("security-headers"))
            {
                AddSecurityHeaderObservation(
                    baseUri,
                    homepage,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("response-time"))
            {
                observations.Add(new Observation(
                    "response-time",
                    "Homepage response time",
                    "Availability",
                    homepage.ResponseTimeMilliseconds <= 2_500
                        ? WebsiteHealthCheckStatus.Passed
                        : WebsiteHealthCheckStatus.Warning,
                    homepage.ResponseTimeMilliseconds,
                    "ms",
                    baseUri.ToString(),
                    $"HTTP {homepage.StatusCode} in {homepage.ResponseTimeMilliseconds:0} ms"));
            }

            if (homepage.IsHtml && homepage.IsSuccess)
            {
                pages[NormalizeUrl(baseUri)] =
                    await AnalyzePageAsync(baseUri, homepage.Content, cancellationToken);
            }

            var robotsUri = new Uri(baseUri, "/robots.txt");
            await DelayAsync(site, cancellationToken);
            var robots = await FetchPageAsync(
                robotsUri,
                site,
                cancellationToken);
            checkedUrls.Add(NormalizeUrl(robotsUri));
            if (enabledCheckKeys.Contains("robots"))
            {
                AddResourceObservation(
                    "robots",
                    "robots.txt",
                    "Discoverability",
                    robotsUri,
                    robots,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("robots-quality"))
            {
                AddRobotsQualityObservation(
                    baseUri,
                    robotsUri,
                    robots,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("sitemap") ||
                enabledCheckKeys.Contains("sitemap-quality"))
            {
                var sitemapUri = new Uri(baseUri, "/sitemap.xml");
                await DelayAsync(site, cancellationToken);
                var sitemap = await FetchPageAsync(
                    sitemapUri,
                    site,
                    cancellationToken);
                checkedUrls.Add(NormalizeUrl(sitemapUri));
                if (enabledCheckKeys.Contains("sitemap"))
                {
                    AddResourceObservation(
                        "sitemap",
                        "Sitemap",
                        "Discoverability",
                        sitemapUri,
                        sitemap,
                        observations,
                        issues);
                }

                if (enabledCheckKeys.Contains("sitemap-quality"))
                {
                    AddSitemapQualityObservation(
                        baseUri,
                        sitemapUri,
                        sitemap,
                        observations,
                        issues);
                }
            }

            var disallowedPaths = ParseRobotsDisallowRules(robots.Content);
            var keyTargets = site.Checks
                .Where(check =>
                    check.IsEnabled &&
                    check.Key == "key-page" &&
                    !string.IsNullOrWhiteSpace(check.TargetPath))
                .Select(check => new Uri(baseUri, check.TargetPath!))
                .Where(uri => IsSameOrigin(baseUri, uri))
                .ToList();

            foreach (var target in keyTargets)
            {
                if (IsDisallowed(target, disallowedPaths))
                {
                    continue;
                }

                await DelayAsync(site, cancellationToken);
                var snapshot = await FetchPageAsync(
                    target,
                    site,
                    cancellationToken);
                checkedUrls.Add(NormalizeUrl(target));
                AddAvailabilityObservation(
                    "key-page",
                    $"Key page: {target.AbsolutePath}",
                    target,
                    snapshot,
                    observations,
                    issues,
                    critical: false);
                if (enabledCheckKeys.Contains("redirect-chain"))
                {
                    AddRedirectObservation(
                        target,
                        snapshot,
                        observations,
                        issues);
                }

                if (snapshot.IsHtml && snapshot.IsSuccess)
                {
                    pages[NormalizeUrl(target)] =
                        await AnalyzePageAsync(target, snapshot.Content, cancellationToken);
                }
            }

            var crawlEnabled = enabledCheckKeys.Overlaps(
                [
                    "internal-link",
                    "redirect-chain",
                    "title",
                    "title-length",
                    "duplicate-title",
                    "heading",
                    "meta-description",
                    "meta-description-length",
                    "duplicate-meta-description",
                    "image-alt",
                    "image-availability",
                    "canonical",
                    "canonical-quality",
                    "indexability",
                    "schema",
                    "schema-quality",
                    "social-preview"
                ]);
            var queue = new Queue<Uri>();
            if (crawlEnabled)
            {
                EnqueueInternalLinks(
                    pages.Values.SelectMany(page => page.InternalLinks),
                    baseUri,
                    disallowedPaths,
                    checkedUrls,
                    queue);
            }

            while (queue.Count > 0 && pages.Count < site.MaxCrawlPages)
            {
                var target = queue.Dequeue();
                var normalized = NormalizeUrl(target);
                if (!checkedUrls.Add(normalized))
                {
                    continue;
                }

                await DelayAsync(site, cancellationToken);
                var snapshot = await FetchPageAsync(
                    target,
                    site,
                    cancellationToken);
                if (enabledCheckKeys.Contains("redirect-chain"))
                {
                    AddRedirectObservation(
                        target,
                        snapshot,
                        observations,
                        issues);
                }

                if (enabledCheckKeys.Contains("internal-link"))
                {
                    AddLinkObservation(target, snapshot, observations, issues);
                }
                if (!snapshot.IsHtml || !snapshot.IsSuccess)
                {
                    continue;
                }

                var page = await AnalyzePageAsync(
                    target,
                    snapshot.Content,
                    cancellationToken);
                pages[normalized] = page;
                EnqueueInternalLinks(
                    page.InternalLinks,
                    baseUri,
                    disallowedPaths,
                    checkedUrls,
                    queue);
            }

            AddContentObservations(
                pages.Values,
                enabledCheckKeys,
                observations,
                issues);
            if (enabledCheckKeys.Contains("image-availability"))
            {
                await CheckImageAvailabilityAsync(
                    pages.Values,
                    site,
                    observations,
                    issues,
                    cancellationToken);
            }

            ApplyScores(run, observations);
            run.PagesCrawled = pages.Count;
            run.LinksChecked = checkedUrls.Count;
            run.CompletedAtUtc = DateTime.UtcNow;
            run.Status = run.OverallScore switch
            {
                >= 90 when issues.All(issue =>
                    issue.Severity != WebsiteHealthIssueSeverity.Critical) =>
                    WebsiteHealthRunStatus.Healthy,
                > 0 => WebsiteHealthRunStatus.Degraded,
                _ => WebsiteHealthRunStatus.Failed
            };
            site.LastCheckedAtUtc = run.CompletedAtUtc;

            AddMetrics(dbContext, site, run, observations);
            await SynchronizeIssuesAsync(
                dbContext,
                site.Id,
                run.Id,
                issues,
                run.CompletedAtUtc.Value,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return run;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            run.Status = WebsiteHealthRunStatus.Failed;
            run.ErrorMessage = "The website check timed out.";
            run.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            return run;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Website health run {RunId} failed for site {SiteId}.",
                run.Id,
                siteId);
            run.Status = WebsiteHealthRunStatus.Failed;
            run.ErrorMessage = Truncate(exception.Message, 2000);
            run.CompletedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            return run;
        }
    }

    private static async Task<Uri> ValidateTargetAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.IsDefaultPort is false)
        {
            throw new InvalidOperationException(
                "Monitored sites must use an HTTPS URL on the default port.");
        }

        await ValidatePublicTargetAsync(uri, cancellationToken);

        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static async Task ValidatePublicTargetAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort)
        {
            throw new InvalidOperationException(
                "Website Health only follows HTTPS redirects on the default port.");
        }

        var addresses = await Dns.GetHostAddressesAsync(
            uri.DnsSafeHost,
            cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new InvalidOperationException(
                "The monitored hostname must resolve only to public addresses.");
        }
    }

    internal static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc;
        }

        var octets = address.GetAddressBytes();
        return octets[0] == 10 ||
            octets[0] == 127 ||
            (octets[0] == 169 && octets[1] == 254) ||
            (octets[0] == 172 && octets[1] is >= 16 and <= 31) ||
            (octets[0] == 192 && octets[1] == 168);
    }

    private async Task CheckSslAsync(
        Uri target,
        MonitoredSite site,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(site.RequestTimeoutSeconds));
        SslPolicyErrors validationErrors = SslPolicyErrors.None;
        using var client = new TcpClient();
        await client.ConnectAsync(target.DnsSafeHost, 443, timeout.Token);
        await using var sslStream = new SslStream(
            client.GetStream(),
            false,
            (_, _, _, errors) =>
            {
                validationErrors = errors;
                return true;
            });
        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = target.DnsSafeHost,
                EnabledSslProtocols =
                    System.Security.Authentication.SslProtocols.Tls12 |
                    System.Security.Authentication.SslProtocols.Tls13
            },
            timeout.Token);

        if (sslStream.RemoteCertificate is null)
        {
            AddFailure(
                "ssl",
                "SSL certificate",
                "Security",
                target,
                "No certificate was presented.",
                WebsiteHealthIssueSeverity.Critical,
                observations,
                issues);
            return;
        }

        using var certificate = new X509Certificate2(sslStream.RemoteCertificate);
        var expiresAt = certificate.NotAfter.ToUniversalTime();
        var daysRemaining = Math.Floor((expiresAt - DateTime.UtcNow).TotalDays);
        var status = validationErrors != SslPolicyErrors.None || daysRemaining < 0
            ? WebsiteHealthCheckStatus.Failed
            : daysRemaining < 30
                ? WebsiteHealthCheckStatus.Warning
                : WebsiteHealthCheckStatus.Passed;
        observations.Add(new Observation(
            "ssl",
            "SSL certificate",
            "Security",
            status,
            (decimal)daysRemaining,
            "days",
            target.ToString(),
            $"Expires {expiresAt:d} ({validationErrors})."));

        if (status != WebsiteHealthCheckStatus.Passed)
        {
            issues.Add(new DetectedIssue(
                "ssl",
                status == WebsiteHealthCheckStatus.Failed
                    ? "SSL certificate is invalid"
                    : "SSL certificate expires soon",
                $"Certificate expires {expiresAt:u}. Validation: {validationErrors}.",
                target.ToString(),
                status == WebsiteHealthCheckStatus.Failed
                    ? WebsiteHealthIssueSeverity.Critical
                    : WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.AvailabilityFailure(
                    "ssl",
                    target)));
        }
    }

    private async Task<FetchSnapshot> FetchPageAsync(
        Uri target,
        MonitoredSite site,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(site.RequestTimeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var currentTarget = target;
            for (var redirectCount = 0; redirectCount <= 5; redirectCount++)
            {
                await ValidatePublicTargetAsync(currentTarget, timeout.Token);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    currentTarget);
                request.Headers.Accept.ParseAdd(
                    "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8");
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);

                if ((int)response.StatusCode is >= 300 and <= 399 &&
                    response.Headers.Location is not null)
                {
                    currentTarget = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentTarget, response.Headers.Location);
                    continue;
                }

                stopwatch.Stop();
                var mediaType =
                    response.Content.Headers.ContentType?.MediaType ?? "";
                var shouldRead = mediaType.StartsWith(
                        "text/",
                        StringComparison.OrdinalIgnoreCase) ||
                    mediaType.Contains(
                        "html",
                        StringComparison.OrdinalIgnoreCase) ||
                    mediaType.Contains(
                        "xml",
                        StringComparison.OrdinalIgnoreCase);
                var content = shouldRead
                    ? await ReadBoundedContentAsync(
                        response.Content,
                        timeout.Token)
                    : string.Empty;
                var headers = response.Headers
                    .Concat(response.Content.Headers)
                    .GroupBy(
                        header => header.Key,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => string.Join(
                            ", ",
                            group.SelectMany(header => header.Value)),
                        StringComparer.OrdinalIgnoreCase);
                return new FetchSnapshot(
                    (int)response.StatusCode,
                    response.IsSuccessStatusCode,
                    mediaType.Contains(
                        "html",
                        StringComparison.OrdinalIgnoreCase),
                    stopwatch.Elapsed.TotalMilliseconds,
                    content,
                    null,
                    currentTarget,
                    redirectCount,
                    headers);
            }

            stopwatch.Stop();
            return new FetchSnapshot(
                null,
                false,
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                string.Empty,
                "The URL exceeded the five-redirect safety limit.",
                currentTarget,
                6);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                OperationCanceledException or
                InvalidOperationException)
        {
            stopwatch.Stop();
            return new FetchSnapshot(
                null,
                false,
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                string.Empty,
                exception is OperationCanceledException
                    ? "Request timed out."
                    : exception.Message,
                target,
                0);
        }
    }

    private static async Task<string> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 8192,
            leaveOpen: false);
        var buffer = new char[8192];
        var result = new StringBuilder();
        while (result.Length < MaximumResponseCharacters)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            result.Append(
                buffer,
                0,
                Math.Min(read, MaximumResponseCharacters - result.Length));
        }

        return result.ToString();
    }

    private async Task<PageSnapshot> AnalyzePageAsync(
        Uri url,
        string html,
        CancellationToken cancellationToken)
    {
        var document = await _htmlParser.ParseDocumentAsync(
            html,
            cancellationToken);
        var links = document.QuerySelectorAll("a[href]")
            .Select(anchor => anchor.GetAttribute("href"))
            .Where(href => !string.IsNullOrWhiteSpace(href))
            .Select(href => ResolveLink(url, href!))
            .Where(link => link is not null)
            .Cast<Uri>()
            .ToHashSet();
        var missingImages = document.QuerySelectorAll("img")
            .Where(ShouldReportMissingImageAlt)
            .Select(image => new WebsiteHealthMissingImage(
                image.GetAttribute("src") ??
                    image.GetAttribute("data-src"),
                GetImageContext(image, url),
                url.ToString()))
            .ToList();
        var images = document.QuerySelectorAll("img[alt]")
            .Select(image => new WebsiteHealthImage(
                image.GetAttribute("src") ??
                    image.GetAttribute("data-src"),
                image.GetAttribute("alt")?.Trim() ?? "",
                GetImageContext(image, url),
                url.ToString(),
                IsBrandLogo(image)))
            .Where(image =>
                !string.IsNullOrWhiteSpace(image.AltText))
            .ToList();
        var imageSources = document.QuerySelectorAll("img")
            .Select(GetPreferredImageSource)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => ResolveLink(url, source!))
            .Where(source =>
                source is not null &&
                source.Scheme == Uri.UriSchemeHttps &&
                source.IsDefaultPort)
            .Cast<Uri>()
            .ToHashSet();
        var introductoryText = document.QuerySelectorAll("main p, article p")
            .Select(element => element.TextContent?.Trim())
            .FirstOrDefault(text => text?.Length >= 50) ??
            document.QuerySelectorAll("p")
                .Select(element => element.TextContent?.Trim())
                .FirstOrDefault(text => text?.Length >= 50);
        var primaryHeading = document.QuerySelector("h1");
        var structuredData = AnalyzeStructuredData(
            document.QuerySelectorAll(
                    "script[type='application/ld+json']")
                .Select(element => element.TextContent));

        return new PageSnapshot(
            url,
            document.Title?.Trim(),
            GetMeaningfulHeadingText(
                primaryHeading?.TextContent,
                primaryHeading?.GetAttribute("aria-label"),
                primaryHeading?.QuerySelectorAll("img[alt]")
                    .Select(image => image.GetAttribute("alt")) ??
                    []),
            document.QuerySelectorAll("h1").Length,
            introductoryText,
            document.QuerySelector("meta[name='description']")
                ?.GetAttribute("content")?.Trim(),
            document.QuerySelector("link[rel~='canonical']")
                ?.GetAttribute("href")?.Trim(),
            structuredData,
            GetMetadataContent(document, "property", "og:title"),
            GetMetadataContent(document, "property", "og:description"),
            GetMetadataContent(document, "property", "og:image:secure_url") ??
                GetMetadataContent(document, "property", "og:image"),
            GetMetadataContent(document, "property", "og:url"),
            GetMetadataContent(document, "name", "twitter:card"),
            document.QuerySelector("meta[name='robots']")
                ?.GetAttribute("content")?.Trim(),
            document.QuerySelector("meta[name='robots']")
                ?.GetAttribute("content")?.Contains(
                    "noindex",
                    StringComparison.OrdinalIgnoreCase) == true,
            missingImages,
            images,
            imageSources,
            links);
    }

    private static string? GetPreferredImageSource(IElement image)
    {
        var candidates = new[]
        {
            image.GetAttribute("data-src"),
            image.GetAttribute("data-lazy-src"),
            image.GetAttribute("src")
        };
        var direct = candidates.FirstOrDefault(source =>
            !string.IsNullOrWhiteSpace(source) &&
            !source.StartsWith(
                "data:",
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var srcset = image.GetAttribute("data-srcset") ??
            image.GetAttribute("srcset");
        return srcset?
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(candidate =>
                candidate.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries)[0])
            .FirstOrDefault(source =>
                !source.StartsWith(
                    "data:",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetMetadataContent(
        IDocument document,
        string attribute,
        string value) =>
        document.QuerySelector(
                $"meta[{attribute}='{value}']")
            ?.GetAttribute("content")?.Trim();

    internal static string? GetMeaningfulHeadingText(
        string? textContent,
        string? ariaLabel,
        IEnumerable<string?> imageAltTexts)
    {
        var candidates = new[] { textContent, ariaLabel }
            .Concat(imageAltTexts);
        return candidates
            .Select(candidate => string.Join(
                ' ',
                (candidate ?? "").Split(
                    [' ', '\r', '\n', '\t'],
                    StringSplitOptions.RemoveEmptyEntries)))
            .FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(candidate));
    }

    private static bool ShouldReportMissingImageAlt(IElement image)
    {
        var interactiveAncestor = FindInteractiveAncestor(image);
        var interactiveHasAccessibleName =
            interactiveAncestor is not null &&
            (!string.IsNullOrWhiteSpace(
                interactiveAncestor.GetAttribute("aria-label")) ||
            !string.IsNullOrWhiteSpace(
                interactiveAncestor.GetAttribute("aria-labelledby")) ||
            !string.IsNullOrWhiteSpace(
                interactiveAncestor.GetAttribute("title")) ||
            !string.IsNullOrWhiteSpace(
                interactiveAncestor.TextContent));

        return IsMissingAlternativeText(
            image.HasAttribute("alt"),
            image.GetAttribute("alt"),
            interactiveAncestor is not null,
            interactiveHasAccessibleName);
    }

    internal static bool IsMissingAlternativeText(
        bool hasAltAttribute,
        string? altText,
        bool isInsideInteractiveControl,
        bool interactiveControlHasAccessibleName)
    {
        if (!hasAltAttribute)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(altText))
        {
            return false;
        }

        return isInsideInteractiveControl &&
            !interactiveControlHasAccessibleName;
    }

    private static IElement? FindInteractiveAncestor(IElement image)
    {
        for (var ancestor = image.ParentElement;
            ancestor is not null;
            ancestor = ancestor.ParentElement)
        {
            if (ancestor.LocalName is "a" or "button")
            {
                return ancestor;
            }
        }

        return null;
    }

    internal static string? GetImageContext(
        IElement image,
        Uri pageUrl)
    {
        var currentAlt = NormalizeImageContext(
            image.GetAttribute("alt"));
        var interactive = FindInteractiveAncestor(image);
        var candidates = new List<string?>
        {
            image.GetAttribute("data-product-title"),
            image.GetAttribute("data-title"),
            image.GetAttribute("title"),
            image.GetAttribute("aria-label"),
            interactive?.GetAttribute("aria-label"),
            interactive?.GetAttribute("title"),
            image.Closest("figure")?.QuerySelector("figcaption")
                ?.TextContent,
            GetLinkedImageTopic(interactive, pageUrl)
        };
        var ancestor = image.ParentElement;
        for (var depth = 0;
            ancestor is not null && depth < 10;
            ancestor = ancestor.ParentElement, depth++)
        {
            candidates.Add(ancestor.GetAttribute("data-product-title"));
            candidates.Add(ancestor.GetAttribute("data-title"));
            candidates.Add(ancestor.GetAttribute("aria-label"));
            candidates.Add(
                ancestor.QuerySelector(
                    "[data-product-title], [data-title], h1, h2, h3, h4")
                    ?.TextContent);
        }

        return candidates
            .Select(NormalizeImageContext)
            .FirstOrDefault(candidate =>
                candidate.Length is >= 3 and <= 125 &&
                !candidate.Equals(
                    currentAlt,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBrandLogo(IElement image)
    {
        var className = image.GetAttribute("class") ?? "";
        if (className.Contains("logo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var interactive = FindInteractiveAncestor(image);
        return image.Closest("header") is not null &&
            interactive?.GetAttribute("href")?.TrimEnd('/') is "";
    }

    private static string? GetLinkedImageTopic(
        IElement? interactive,
        Uri pageUrl)
    {
        var href = interactive?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href) ||
            !Uri.TryCreate(pageUrl, href, out var linked))
        {
            return null;
        }

        var segment = linked.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            Uri.UnescapeDataString(segment)
                .Replace('-', ' ')
                .Replace('_', ' '));
    }

    private static string NormalizeImageContext(string? value) =>
        string.Join(
            ' ',
            WebUtility.HtmlDecode(value ?? "")
                .Split(
                    [' ', '\r', '\n', '\t'],
                    StringSplitOptions.RemoveEmptyEntries));

    private static Uri? ResolveLink(Uri currentPage, string href)
    {
        if (href.StartsWith('#') ||
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(currentPage, href, out var resolved) ||
            resolved.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return new UriBuilder(resolved) { Fragment = string.Empty }.Uri;
    }

    private static void EnqueueInternalLinks(
        IEnumerable<Uri> links,
        Uri baseUri,
        IReadOnlyCollection<string> disallowedPaths,
        IReadOnlySet<string> checkedUrls,
        Queue<Uri> queue)
    {
        var queued = queue.Select(NormalizeUrl).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (var link in links
            .Where(link =>
                IsSameOrigin(baseUri, link) &&
                !IsDisallowed(link, disallowedPaths))
            .OrderBy(link => link.AbsolutePath))
        {
            var normalized = NormalizeUrl(link);
            if (!checkedUrls.Contains(normalized) && queued.Add(normalized))
            {
                queue.Enqueue(link);
            }
        }
    }

    internal static IReadOnlyCollection<string> ParseRobotsDisallowRules(
        string content)
    {
        var rules = new List<string>();
        var applies = false;
        foreach (var line in content.Split('\n'))
        {
            var cleaned = line.Split('#', 2)[0].Trim();
            if (cleaned.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                applies = cleaned["User-agent:".Length..].Trim() == "*";
            }
            else if (applies &&
                cleaned.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
            {
                var path = cleaned["Disallow:".Length..].Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    rules.Add(path);
                }
            }
        }

        return rules;
    }

    internal static IReadOnlyCollection<Uri> ParseRobotsSitemapLocations(
        string content)
    {
        var locations = new List<Uri>();
        foreach (var line in content.Split('\n'))
        {
            var cleaned = line.Split('#', 2)[0].Trim();
            if (!cleaned.StartsWith(
                    "Sitemap:",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = cleaned["Sitemap:".Length..].Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var location))
            {
                locations.Add(location);
            }
        }

        return locations;
    }

    internal static SitemapAnalysis AnalyzeSitemap(
        string content,
        Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new SitemapAnalysis(
                false,
                false,
                0,
                0,
                0,
                "The sitemap response is empty.");
        }

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(content),
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumResponseCharacters
                });
            var document = XDocument.Load(reader);
            var rootName = document.Root?.Name.LocalName;
            var hasSupportedRoot =
                rootName is "urlset" or "sitemapindex";
            var locations = document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName.Equals(
                        "loc",
                        StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var validLocations = locations
                .Select(value =>
                    Uri.TryCreate(
                        value,
                        UriKind.Absolute,
                        out var location)
                        ? location
                        : null)
                .ToList();
            var invalidCount = validLocations.Count(location =>
                location is null ||
                location.Scheme != Uri.UriSchemeHttps);
            var externalCount = validLocations.Count(location =>
                location is not null &&
                !NormalizeCanonicalHost(location.Host).Equals(
                    NormalizeCanonicalHost(baseUri.Host),
                    StringComparison.OrdinalIgnoreCase));
            return new SitemapAnalysis(
                true,
                hasSupportedRoot,
                locations.Count,
                invalidCount,
                externalCount,
                hasSupportedRoot
                    ? null
                    : $"Unexpected root element: {rootName ?? "(missing)"}.");
        }
        catch (XmlException exception)
        {
            return new SitemapAnalysis(
                false,
                false,
                0,
                0,
                0,
                Truncate(exception.Message, 300));
        }
    }

    internal static bool IsDisallowed(
        Uri target,
        IReadOnlyCollection<string> rules) =>
        rules.Any(rule =>
            rule == "/" ||
            target.PathAndQuery.StartsWith(
                rule.TrimEnd('*'),
                StringComparison.OrdinalIgnoreCase));

    private static void AddAvailabilityObservation(
        string key,
        string label,
        Uri target,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues,
        bool critical)
    {
        if (snapshot.IsSuccess)
        {
            observations.Add(new Observation(
                key,
                label,
                "Availability",
                WebsiteHealthCheckStatus.Passed,
                snapshot.StatusCode,
                "HTTP",
                target.ToString(),
                $"HTTP {snapshot.StatusCode}."));
            return;
        }

        AddFailure(
            key,
            label,
            "Availability",
            target,
            snapshot.Error ?? $"Returned HTTP {snapshot.StatusCode}.",
            critical
                ? WebsiteHealthIssueSeverity.Critical
                : WebsiteHealthIssueSeverity.Warning,
            observations,
            issues);
    }

    private static void AddResourceObservation(
        string key,
        string label,
        string category,
        Uri target,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        if (snapshot.IsSuccess && !string.IsNullOrWhiteSpace(snapshot.Content))
        {
            observations.Add(new Observation(
                key,
                label,
                category,
                WebsiteHealthCheckStatus.Passed,
                snapshot.StatusCode,
                "HTTP",
                target.ToString(),
                $"HTTP {snapshot.StatusCode}."));
            return;
        }

        AddFailure(
            key,
            label,
            category,
            target,
            snapshot.Error ?? $"Returned HTTP {snapshot.StatusCode}.",
            WebsiteHealthIssueSeverity.Warning,
            observations,
            issues);
    }

    private static void AddRobotsQualityObservation(
        Uri baseUri,
        Uri robotsUri,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var disallowRules = ParseRobotsDisallowRules(snapshot.Content);
        var sitemapLocations =
            ParseRobotsSitemapLocations(snapshot.Content);
        var blocksStorefront = disallowRules.Any(rule =>
            rule.Trim() == "/");
        var hasProductionSitemap = sitemapLocations.Any(location =>
            location.Scheme == Uri.UriSchemeHttps &&
            NormalizeCanonicalHost(location.Host).Equals(
                NormalizeCanonicalHost(baseUri.Host),
                StringComparison.OrdinalIgnoreCase) &&
            location.AbsolutePath.Equals(
                "/sitemap.xml",
                StringComparison.OrdinalIgnoreCase));
        var healthy =
            snapshot.IsSuccess &&
            !blocksStorefront &&
            hasProductionSitemap;
        var problems = new List<string>();
        if (!snapshot.IsSuccess)
        {
            problems.Add("robots.txt is unavailable");
        }

        if (blocksStorefront)
        {
            problems.Add("User-agent * disallows the entire storefront");
        }

        if (!hasProductionSitemap)
        {
            problems.Add("the production sitemap declaration is missing");
        }

        observations.Add(new Observation(
            "robots-quality",
            "robots.txt quality",
            "Discoverability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            healthy ? 0m : problems.Count,
            "issues",
            robotsUri.ToString(),
            healthy
                ? "The storefront is crawlable and the production sitemap is declared."
                : string.Join("; ", problems) + "."));
        if (!healthy)
        {
            issues.Add(new DetectedIssue(
                "robots-quality",
                "robots.txt needs improvement",
                string.Join("; ", problems) + ".",
                robotsUri.ToString(),
                blocksStorefront
                    ? WebsiteHealthIssueSeverity.Critical
                    : WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.RobotsQuality(
                    baseUri,
                    blocksStorefront,
                    hasProductionSitemap)));
        }
    }

    private static void AddSitemapQualityObservation(
        Uri baseUri,
        Uri sitemapUri,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var analysis = AnalyzeSitemap(
            snapshot.Content,
            baseUri);
        var healthy =
            snapshot.IsSuccess &&
            analysis.IsValidXml &&
            analysis.HasSupportedRoot &&
            analysis.LocationCount > 0 &&
            analysis.InvalidLocationCount == 0 &&
            analysis.ExternalLocationCount == 0;
        var problems = new List<string>();
        if (!snapshot.IsSuccess)
        {
            problems.Add("sitemap.xml is unavailable");
        }
        else if (!analysis.IsValidXml)
        {
            problems.Add($"the XML is invalid: {analysis.Error}");
        }
        else
        {
            if (!analysis.HasSupportedRoot)
            {
                problems.Add(analysis.Error ?? "the XML root is unsupported");
            }

            if (analysis.LocationCount == 0)
            {
                problems.Add("no sitemap locations were found");
            }

            if (analysis.InvalidLocationCount > 0)
            {
                problems.Add(
                    $"{analysis.InvalidLocationCount} location(s) are not absolute HTTPS URLs");
            }

            if (analysis.ExternalLocationCount > 0)
            {
                problems.Add(
                    $"{analysis.ExternalLocationCount} location(s) use another domain");
            }
        }

        observations.Add(new Observation(
            "sitemap-quality",
            "Sitemap quality",
            "Discoverability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)(healthy
                ? analysis.LocationCount
                : problems.Count),
            healthy ? "locations" : "issues",
            sitemapUri.ToString(),
            healthy
                ? $"Valid sitemap XML with {analysis.LocationCount} location(s)."
                : string.Join("; ", problems) + "."));
        if (!healthy)
        {
            issues.Add(new DetectedIssue(
                "sitemap-quality",
                "Sitemap needs improvement",
                string.Join("; ", problems) + ".",
                sitemapUri.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.SitemapQuality(
                    sitemapUri,
                    analysis)));
        }
    }

    private static void AddLinkObservation(
        Uri target,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        observations.Add(new Observation(
            "internal-link",
            "Internal link",
            "Availability",
            snapshot.IsSuccess
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Failed,
            snapshot.StatusCode,
            "HTTP",
            target.ToString(),
            snapshot.Error ?? $"HTTP {snapshot.StatusCode}."));
        if (!snapshot.IsSuccess)
        {
            issues.Add(new DetectedIssue(
                "internal-link",
                "Broken internal link",
                snapshot.Error ?? $"The URL returned HTTP {snapshot.StatusCode}.",
                target.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.BrokenLink(
                    target,
                    snapshot.StatusCode)));
        }
    }

    private static void AddRedirectObservation(
        Uri requestedUrl,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var finalUrl = snapshot.FinalUri ?? requestedUrl;
        var healthy = IsRedirectChainHealthy(
            requestedUrl,
            finalUrl,
            snapshot.RedirectCount);
        var detail = snapshot.RedirectCount switch
        {
            0 => "No redirect.",
            1 when healthy =>
                $"One canonical redirect to {finalUrl}.",
            _ =>
                $"{snapshot.RedirectCount} redirects; final URL: {finalUrl}."
        };
        observations.Add(new Observation(
            "redirect-chain",
            "Redirect chain",
            "Availability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)snapshot.RedirectCount,
            "redirects",
            requestedUrl.ToString(),
            detail));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "redirect-chain",
            "Redirect chain is too long",
            $"This URL requires {snapshot.RedirectCount} redirects before reaching {finalUrl}.",
            requestedUrl.ToString(),
            WebsiteHealthIssueSeverity.Warning,
            WebsiteHealthRecommendationBuilder.RedirectChain(
                requestedUrl,
                finalUrl,
                snapshot.RedirectCount)));
    }

    internal static bool IsRedirectChainHealthy(
        Uri requestedUrl,
        Uri finalUrl,
        int redirectCount)
    {
        if (redirectCount == 0)
        {
            return true;
        }

        if (redirectCount != 1 ||
            finalUrl.Scheme != Uri.UriSchemeHttps ||
            !NormalizeCanonicalHost(requestedUrl.Host).Equals(
                NormalizeCanonicalHost(finalUrl.Host),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return requestedUrl.AbsolutePath.TrimEnd('/').Equals(
            finalUrl.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);
    }

    private async Task CheckImageAvailabilityAsync(
        IEnumerable<PageSnapshot> pages,
        MonitoredSite site,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues,
        CancellationToken cancellationToken)
    {
        var assets = pages
            .SelectMany(page =>
                page.ImageSources.Select(source => new
                {
                    Source = source,
                    Page = page.Url
                }))
            .GroupBy(
                item => NormalizeImageAssetUrl(
                    item.Page,
                    item.Source.ToString()) ??
                    item.Source.GetLeftPart(UriPartial.Path),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Source = group.First().Source,
                Pages = group
                    .Select(item => item.Page)
                    .Distinct()
                    .OrderBy(page => page.AbsolutePath)
                    .ToList()
            })
            .OrderBy(asset => asset.Source.Host)
            .ThenBy(asset => asset.Source.AbsolutePath)
            .Take(Math.Min(site.MaxCrawlPages, 25))
            .ToList();

        foreach (var asset in assets)
        {
            await DelayAsync(site, cancellationToken);
            var snapshot = await FetchPageAsync(
                asset.Source,
                site,
                cancellationToken);
            observations.Add(new Observation(
                "image-availability",
                "Image asset",
                "Content",
                snapshot.IsSuccess
                    ? WebsiteHealthCheckStatus.Passed
                    : WebsiteHealthCheckStatus.Warning,
                snapshot.StatusCode,
                "HTTP",
                asset.Source.ToString(),
                snapshot.Error ??
                    $"HTTP {snapshot.StatusCode}; used on {asset.Pages.Count} crawled page(s)."));
            if (snapshot.IsSuccess)
            {
                continue;
            }

            issues.Add(new DetectedIssue(
                "image-availability",
                "Image asset does not load",
                snapshot.Error ??
                    $"The image returned HTTP {snapshot.StatusCode} and appears on {asset.Pages.Count} crawled page(s).",
                asset.Source.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.BrokenImage(
                    asset.Source,
                    asset.Pages,
                    snapshot.StatusCode)));
        }
    }

    private static void AddSecurityHeaderObservation(
        Uri pageUrl,
        FetchSnapshot snapshot,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var analysis = AnalyzeSecurityHeaders(
            snapshot.Headers ??
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase));
        observations.Add(new Observation(
            "security-headers",
            "Storefront security headers",
            "Security",
            analysis.IsHealthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)analysis.MissingHeaders.Count,
            "missing protections",
            pageUrl.ToString(),
            analysis.IsHealthy
                ? "HTTPS enforcement, MIME-sniffing protection, framing protection, and Content Security Policy are present."
                : $"Missing or invalid: {string.Join(", ", analysis.MissingHeaders)}."));
        if (analysis.IsHealthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "security-headers",
            "Storefront security headers need review",
            $"Missing or invalid: {string.Join(", ", analysis.MissingHeaders)}.",
            pageUrl.ToString(),
            !analysis.HasStrictTransportSecurity ||
                !analysis.HasFramingProtection
                ? WebsiteHealthIssueSeverity.Critical
                : WebsiteHealthIssueSeverity.Warning,
            WebsiteHealthRecommendationBuilder.SecurityHeaders(
                pageUrl,
                analysis.MissingHeaders)));
    }

    internal static SecurityHeaderAnalysis AnalyzeSecurityHeaders(
        IReadOnlyDictionary<string, string> headers)
    {
        string? GetHeader(string name) =>
            headers.FirstOrDefault(header =>
                header.Key.Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase)).Value;

        var strictTransportSecurity =
            GetHeader("Strict-Transport-Security");
        var contentTypeOptions =
            GetHeader("X-Content-Type-Options");
        var frameOptions = GetHeader("X-Frame-Options");
        var contentSecurityPolicy =
            GetHeader("Content-Security-Policy");
        var hasStrictTransportSecurity =
            !string.IsNullOrWhiteSpace(strictTransportSecurity) &&
            Regex.IsMatch(
                strictTransportSecurity,
                @"(?:^|;)\s*max-age\s*=\s*[1-9]\d*",
                RegexOptions.IgnoreCase);
        var hasContentTypeProtection =
            contentTypeOptions?.Contains(
                "nosniff",
                StringComparison.OrdinalIgnoreCase) == true;
        var hasContentSecurityPolicy =
            !string.IsNullOrWhiteSpace(contentSecurityPolicy);
        var hasFramingProtection =
            frameOptions?.Contains(
                "DENY",
                StringComparison.OrdinalIgnoreCase) == true ||
            frameOptions?.Contains(
                "SAMEORIGIN",
                StringComparison.OrdinalIgnoreCase) == true ||
            contentSecurityPolicy?.Contains(
                "frame-ancestors",
                StringComparison.OrdinalIgnoreCase) == true;
        var missing = new List<string>();
        if (!hasStrictTransportSecurity)
        {
            missing.Add("Strict-Transport-Security");
        }

        if (!hasContentTypeProtection)
        {
            missing.Add("X-Content-Type-Options: nosniff");
        }

        if (!hasFramingProtection)
        {
            missing.Add("framing protection");
        }

        if (!hasContentSecurityPolicy)
        {
            missing.Add("Content-Security-Policy");
        }

        return new SecurityHeaderAnalysis(
            hasStrictTransportSecurity,
            hasContentTypeProtection,
            hasFramingProtection,
            hasContentSecurityPolicy,
            missing);
    }

    private static void AddContentObservations(
        IEnumerable<PageSnapshot> pages,
        IReadOnlySet<string> enabledCheckKeys,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var pageSnapshots = pages.ToList();
        foreach (var page in pageSnapshots)
        {
            if (enabledCheckKeys.Contains("title"))
            {
                AddPresenceObservation(
                    "title",
                    "Page title",
                    "Content",
                    page.Url,
                    !string.IsNullOrWhiteSpace(page.Title),
                    "Missing page title",
                    observations,
                    issues,
                    recommendation:
                        WebsiteHealthRecommendationBuilder.MissingTitle(
                            page.Url,
                            page.Heading));
            }

            if (enabledCheckKeys.Contains("title-length") &&
                !page.IsNoIndex &&
                !string.IsNullOrWhiteSpace(page.Title))
            {
                AddMetadataLengthObservation(
                    "title-length",
                    "Page title length",
                    page.Url,
                    page.Title,
                    20,
                    60,
                    observations,
                    issues,
                    WebsiteHealthRecommendationBuilder.TitleLength(
                        page.Url,
                        page.Heading,
                        page.Title));
            }

            if (enabledCheckKeys.Contains("heading"))
            {
                AddHeadingObservation(
                    page,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("meta-description") &&
                !page.IsNoIndex)
            {
                AddPresenceObservation(
                    "meta-description",
                    "Meta description",
                    "Content",
                    page.Url,
                    !string.IsNullOrWhiteSpace(page.MetaDescription),
                    "Missing meta description",
                    observations,
                    issues,
                    recommendation:
                        WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                            page.Url,
                            page.Title,
                            page.Heading,
                            page.IntroductoryText),
                    createIssue: false);
            }

            if (enabledCheckKeys.Contains("meta-description-length") &&
                !page.IsNoIndex &&
                !string.IsNullOrWhiteSpace(page.MetaDescription))
            {
                AddMetadataLengthObservation(
                    "meta-description-length",
                    "Meta description length",
                    page.Url,
                    page.MetaDescription,
                    70,
                    160,
                    observations,
                    issues,
                    WebsiteHealthRecommendationBuilder.MetaDescriptionLength(
                        page.Url,
                        page.Title,
                        page.Heading,
                        page.IntroductoryText,
                        page.MetaDescription));
            }

            if (enabledCheckKeys.Contains("canonical"))
            {
                AddPresenceObservation(
                    "canonical",
                    "Canonical URL",
                    "Discoverability",
                    page.Url,
                    !string.IsNullOrWhiteSpace(page.Canonical),
                    "Missing canonical URL",
                    observations,
                    issues,
                    recommendation:
                        WebsiteHealthRecommendationBuilder.MissingCanonical(
                            page.Url));
            }

            if (enabledCheckKeys.Contains("canonical-quality") &&
                !string.IsNullOrWhiteSpace(page.Canonical))
            {
                AddCanonicalQualityObservation(
                    page,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("indexability") &&
                !WebsiteHealthRecommendationBuilder.IsUtilityPage(page.Url))
            {
                AddIndexabilityObservation(
                    page,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("schema"))
            {
                AddPresenceObservation(
                    "schema",
                    "Structured data",
                    "Discoverability",
                    page.Url,
                    page.StructuredData.BlockCount > 0,
                    "Missing structured data",
                    observations,
                    issues,
                    WebsiteHealthIssueSeverity.Info,
                    WebsiteHealthRecommendationBuilder.MissingSchema(
                        page.Url,
                        page.Title,
                        page.Heading,
                        page.MetaDescription));
            }

            if (enabledCheckKeys.Contains("schema-quality"))
            {
                AddSchemaQualityObservation(
                    page,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("social-preview") &&
                !page.IsNoIndex &&
                !WebsiteHealthRecommendationBuilder.IsUtilityPage(page.Url))
            {
                AddSocialPreviewObservation(
                    page,
                    observations,
                    issues);
            }

            if (enabledCheckKeys.Contains("image-alt"))
            {
                var genericAltCount = page.Images.Count(image =>
                    IsGenericImageAlt(
                        image.AltText,
                        image.Source));
                var issueCount =
                    page.MissingImages.Count + genericAltCount;
                observations.Add(new Observation(
                    "image-alt",
                    "Image alt text",
                    "Content",
                    issueCount == 0
                        ? WebsiteHealthCheckStatus.Passed
                        : WebsiteHealthCheckStatus.Warning,
                    (decimal)issueCount,
                    "images",
                    page.Url.ToString(),
                    issueCount == 0
                        ? "All meaningful images have descriptive alt text."
                        : $"{page.MissingImages.Count} image(s) are missing alt text; " +
                            $"{genericAltCount} image(s) have generic alt text."));
            }
        }

        if (enabledCheckKeys.Contains("image-alt"))
        {
            AddGroupedImageAltIssues(pageSnapshots, issues);
            AddImageAltQualityIssues(pageSnapshots, issues);
        }

        if (enabledCheckKeys.Contains("meta-description"))
        {
            AddGroupedMetaDescriptionIssues(pageSnapshots, issues);
        }

        if (enabledCheckKeys.Contains("duplicate-title"))
        {
            AddDuplicateMetadataIssues(
                pageSnapshots,
                "duplicate-title",
                "Duplicate page title",
                page => page.Title,
                (page, target, matchingUrl) =>
                    WebsiteHealthRecommendationBuilder.DuplicateTitle(
                        target,
                        page.Heading,
                        matchingUrl),
                observations,
                issues);
        }

        if (enabledCheckKeys.Contains("duplicate-meta-description"))
        {
            AddDuplicateMetadataIssues(
                pageSnapshots,
                "duplicate-meta-description",
                "Duplicate meta description",
                page => page.MetaDescription,
                (page, target, matchingUrl) =>
                    WebsiteHealthRecommendationBuilder
                        .DuplicateMetaDescription(
                            target,
                            page.Title,
                            page.Heading,
                            page.IntroductoryText,
                            matchingUrl),
                observations,
                issues);
        }
    }

    private static void AddSocialPreviewObservation(
        PageSnapshot page,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var missingFields = GetMissingSocialPreviewFields(
            page.OpenGraphTitle,
            page.OpenGraphDescription,
            page.OpenGraphImage,
            page.OpenGraphUrl,
            page.TwitterCard);
        var healthy = missingFields.Count == 0;
        observations.Add(new Observation(
            "social-preview",
            "Social sharing preview",
            "Content",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)missingFields.Count,
            "missing fields",
            page.Url.ToString(),
            healthy
                ? "Open Graph title, description, image, URL, and Twitter card are present."
                : $"Missing: {string.Join(", ", missingFields)}."));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "social-preview",
            "Social sharing preview is incomplete",
            $"The page is missing {string.Join(", ", missingFields)}. Shared links may appear without useful page-specific text or imagery.",
            page.Url.ToString(),
            WebsiteHealthIssueSeverity.Info,
            WebsiteHealthRecommendationBuilder.SocialPreview(
                page.Url,
                page.Title,
                page.Heading,
                page.IntroductoryText,
                page.MetaDescription,
                page.OpenGraphTitle,
                page.OpenGraphDescription,
                page.OpenGraphImage,
                page.OpenGraphUrl,
                page.TwitterCard)));
    }

    internal static IReadOnlyList<string> GetMissingSocialPreviewFields(
        string? openGraphTitle,
        string? openGraphDescription,
        string? openGraphImage,
        string? openGraphUrl,
        string? twitterCard)
    {
        var fields = new List<string>();
        if (string.IsNullOrWhiteSpace(openGraphTitle))
        {
            fields.Add("og:title");
        }

        if (string.IsNullOrWhiteSpace(openGraphDescription))
        {
            fields.Add("og:description");
        }

        if (!Uri.TryCreate(
                openGraphImage,
                UriKind.Absolute,
                out var image) ||
            image.Scheme is not ("http" or "https"))
        {
            fields.Add("og:image");
        }

        if (!Uri.TryCreate(
                openGraphUrl,
                UriKind.Absolute,
                out var socialUrl) ||
            socialUrl.Scheme != Uri.UriSchemeHttps)
        {
            fields.Add("og:url");
        }

        if (string.IsNullOrWhiteSpace(twitterCard))
        {
            fields.Add("twitter:card");
        }

        return fields;
    }

    private static void AddHeadingObservation(
        PageSnapshot page,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var healthy = page.HeadingCount == 1 &&
            !string.IsNullOrWhiteSpace(page.Heading);
        observations.Add(new Observation(
            "heading",
            "Primary page heading",
            "Content",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)page.HeadingCount,
            "H1 headings",
            page.Url.ToString(),
            healthy
                ? $"One H1 heading: {page.Heading}"
                : page.HeadingCount == 0
                    ? "No H1 heading was detected."
                    : $"{page.HeadingCount} H1 headings were detected."));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "heading",
            page.HeadingCount <= 1
                ? "Missing or empty primary page heading"
                : "Multiple primary page headings",
            page.HeadingCount <= 1
                ? page.HeadingCount == 0
                    ? "Search engines and customers need one visible heading that clearly identifies this page."
                    : "The page contains an H1 element, but it has no readable text. Add a visible heading that clearly identifies this page."
                : $"This page contains {page.HeadingCount} H1 headings. Keep one primary heading and change the others to H2 or H3.",
            page.Url.ToString(),
            WebsiteHealthIssueSeverity.Warning,
            WebsiteHealthRecommendationBuilder.HeadingStructure(
                page.Url,
                page.Title,
                page.Heading,
                page.HeadingCount)));
    }

    private static void AddSchemaQualityObservation(
        PageSnapshot page,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var missingExpectedType = GetMissingExpectedSchemaType(
            page.Url,
            page.StructuredData.SchemaTypes);
        var productProblems = GetProductSchemaProblems(
            page.Url,
            page.StructuredData.SchemaTypes,
            page.StructuredData.Product);
        var healthy =
            page.StructuredData.InvalidBlockCount == 0 &&
            missingExpectedType is null &&
            productProblems.Count == 0;
        var details = new List<string>();
        if (page.StructuredData.InvalidBlockCount > 0)
        {
            details.Add(
                $"{page.StructuredData.InvalidBlockCount} malformed JSON-LD block(s)");
        }

        if (missingExpectedType is not null)
        {
            details.Add($"{missingExpectedType} schema was not detected");
        }

        details.AddRange(productProblems);

        observations.Add(new Observation(
            "schema-quality",
            "Structured data quality",
            "Discoverability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)(
                page.StructuredData.InvalidBlockCount +
                (missingExpectedType is null ? 0 : 1)),
            "issues",
            page.Url.ToString(),
            healthy
                ? $"Valid JSON-LD: {string.Join(
                    ", ",
                    page.StructuredData.SchemaTypes.OrderBy(type => type))}."
                : string.Join("; ", details) + "."));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "schema-quality",
            "Structured data needs improvement",
            string.Join("; ", details) + ".",
            page.Url.ToString(),
            WebsiteHealthIssueSeverity.Warning,
            WebsiteHealthRecommendationBuilder.SchemaQuality(
                page.Url,
                page.StructuredData.InvalidBlockCount,
                missingExpectedType,
                productProblems)));
    }

    internal static StructuredDataAnalysis AnalyzeStructuredData(
        IEnumerable<string?> blocks)
    {
        var blockCount = 0;
        var validBlockCount = 0;
        var invalidBlockCount = 0;
        var schemaTypes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var productSignals = new ProductSignalAccumulator();
        foreach (var block in blocks)
        {
            blockCount++;
            if (string.IsNullOrWhiteSpace(block))
            {
                invalidBlockCount++;
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(block);
                CollectSchemaTypes(document.RootElement, schemaTypes);
                CollectProductSignals(
                    document.RootElement,
                    productSignals);
                validBlockCount++;
            }
            catch (JsonException)
            {
                invalidBlockCount++;
            }
        }

        return new StructuredDataAnalysis(
            blockCount,
            validBlockCount,
            invalidBlockCount,
            schemaTypes,
            new ProductStructuredDataAnalysis(
                productSignals.HasName,
                productSignals.HasImage,
                productSignals.HasOffers,
                productSignals.HasPrice,
                productSignals.HasPriceCurrency,
                productSignals.HasAvailability,
                productSignals.ProductUrls));
    }

    private static void CollectProductSignals(
        JsonElement element,
        ProductSignalAccumulator signals)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (HasSchemaType(element, "Product"))
            {
                signals.HasName |= HasMeaningfulProperty(
                    element,
                    "name");
                signals.HasImage |= HasMeaningfulProperty(
                    element,
                    "image");
                if (element.TryGetProperty(
                        "offers",
                        out var offers) &&
                    HasMeaningfulValue(offers))
                {
                    signals.HasOffers = true;
                    signals.HasPrice |=
                        ContainsMeaningfulProperty(
                            offers,
                            "price") ||
                        ContainsMeaningfulProperty(
                            offers,
                            "lowPrice");
                    signals.HasPriceCurrency |=
                        ContainsMeaningfulProperty(
                            offers,
                            "priceCurrency");
                    signals.HasAvailability |=
                        ContainsMeaningfulProperty(
                            offers,
                            "availability");
                }

                if (element.TryGetProperty(
                        "url",
                        out var productUrl) &&
                    productUrl.ValueKind == JsonValueKind.String &&
                    productUrl.GetString() is { Length: > 0 } url)
                {
                    signals.ProductUrls.Add(url);
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                CollectProductSignals(
                    property.Value,
                    signals);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectProductSignals(item, signals);
            }
        }
    }

    private static bool HasSchemaType(
        JsonElement element,
        string expectedType)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String =>
                type.GetString()?.Equals(
                    expectedType,
                    StringComparison.OrdinalIgnoreCase) == true,
            JsonValueKind.Array => type
                .EnumerateArray()
                .Any(value =>
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString()?.Equals(
                        expectedType,
                        StringComparison.OrdinalIgnoreCase) == true),
            _ => false
        };
    }

    private static bool HasMeaningfulProperty(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(
            propertyName,
            out var value) &&
        HasMeaningfulValue(value);

    private static bool HasMeaningfulValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String =>
                !string.IsNullOrWhiteSpace(value.GetString()),
            JsonValueKind.Number => true,
            JsonValueKind.Object =>
                value.EnumerateObject().Any(),
            JsonValueKind.Array =>
                value.GetArrayLength() > 0,
            _ => false
        };

    private static bool ContainsMeaningfulProperty(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    HasMeaningfulValue(property.Value))
                {
                    return true;
                }

                if (ContainsMeaningfulProperty(
                        property.Value,
                        propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item =>
                ContainsMeaningfulProperty(
                    item,
                    propertyName));
        }

        return false;
    }

    internal static IReadOnlyList<string> GetProductSchemaProblems(
        Uri pageUrl,
        IReadOnlySet<string> schemaTypes,
        ProductStructuredDataAnalysis product)
    {
        if (!pageUrl.AbsolutePath.StartsWith(
                "/products/",
                StringComparison.OrdinalIgnoreCase) ||
            !schemaTypes.Contains("Product"))
        {
            return [];
        }

        var problems = new List<string>();
        if (!product.HasName)
        {
            problems.Add("Product name is missing");
        }

        if (!product.HasImage)
        {
            problems.Add("Product image is missing");
        }

        if (!product.HasOffers)
        {
            problems.Add("Product offers are missing");
        }
        else
        {
            if (!product.HasPrice)
            {
                problems.Add("Offer price is missing");
            }

            if (!product.HasPriceCurrency)
            {
                problems.Add("Offer priceCurrency is missing");
            }

            if (!product.HasAvailability)
            {
                problems.Add("Offer availability is missing");
            }
        }

        var hasMatchingUrl = product.ProductUrls.Any(value =>
            Uri.TryCreate(
                pageUrl,
                value,
                out var productUrl) &&
            NormalizeCanonicalHost(productUrl.Host).Equals(
                NormalizeCanonicalHost(pageUrl.Host),
                StringComparison.OrdinalIgnoreCase) &&
            productUrl.AbsolutePath.TrimEnd('/').Equals(
                pageUrl.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase));
        if (!hasMatchingUrl)
        {
            problems.Add("Product URL does not match this page");
        }

        return problems;
    }

    private sealed class ProductSignalAccumulator
    {
        internal bool HasName { get; set; }
        internal bool HasImage { get; set; }
        internal bool HasOffers { get; set; }
        internal bool HasPrice { get; set; }
        internal bool HasPriceCurrency { get; set; }
        internal bool HasAvailability { get; set; }
        internal List<string> ProductUrls { get; } = [];
    }

    private static void CollectSchemaTypes(
        JsonElement element,
        ISet<string> schemaTypes)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("@type"))
                {
                    if (property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 } type)
                    {
                        schemaTypes.Add(type);
                    }
                    else if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var typeValue in
                            property.Value.EnumerateArray())
                        {
                            if (typeValue.ValueKind == JsonValueKind.String &&
                                typeValue.GetString() is { Length: > 0 }
                                    arrayType)
                            {
                                schemaTypes.Add(arrayType);
                            }
                        }
                    }
                }

                CollectSchemaTypes(property.Value, schemaTypes);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectSchemaTypes(item, schemaTypes);
            }
        }
    }

    internal static string? GetMissingExpectedSchemaType(
        Uri url,
        IReadOnlySet<string> schemaTypes)
    {
        if (url.AbsolutePath == "/")
        {
            return schemaTypes.Contains("WebSite") ? null : "WebSite";
        }

        if (url.AbsolutePath.StartsWith(
            "/products/",
            StringComparison.OrdinalIgnoreCase))
        {
            return schemaTypes.Contains("Product") ? null : "Product";
        }

        var segments = url.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3 &&
            segments[0].Equals(
                "blogs",
                StringComparison.OrdinalIgnoreCase))
        {
            return schemaTypes.Contains("Article") ||
                schemaTypes.Contains("BlogPosting")
                    ? null
                    : "Article";
        }

        return null;
    }

    private static void AddCanonicalQualityObservation(
        PageSnapshot page,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var healthy = IsCanonicalHealthy(
            page.Url,
            page.Canonical);
        observations.Add(new Observation(
            "canonical-quality",
            "Canonical URL quality",
            "Discoverability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            healthy ? 0m : 1m,
            "issue",
            page.Url.ToString(),
            healthy
                ? "Canonical URL is absolute, secure, and points to this page."
                : $"Canonical value needs review: {page.Canonical}"));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "canonical-quality",
            "Canonical URL needs improvement",
            "The canonical should use HTTPS, stay on the production domain, omit tracking parameters and fragments, and identify this page.",
            page.Url.ToString(),
            WebsiteHealthIssueSeverity.Warning,
            WebsiteHealthRecommendationBuilder.CanonicalQuality(
                page.Url,
                page.Canonical!)));
    }

    private static void AddIndexabilityObservation(
        PageSnapshot page,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var healthy = !page.IsNoIndex;
        observations.Add(new Observation(
            "indexability",
            "Search indexability",
            "Discoverability",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Failed,
            healthy ? 1m : 0m,
            "indexable",
            page.Url.ToString(),
            healthy
                ? "No noindex directive was detected."
                : $"The page is blocked by: {page.RobotsDirective}"));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            "indexability",
            "Search indexing is blocked",
            "This public storefront page contains a noindex directive, so search engines are being told not to include it in results.",
            page.Url.ToString(),
            WebsiteHealthIssueSeverity.Critical,
            WebsiteHealthRecommendationBuilder.SearchIndexability(
                page.Url,
                page.RobotsDirective)));
    }

    internal static bool IsCanonicalHealthy(
        Uri pageUrl,
        string? canonical)
    {
        if (!Uri.TryCreate(canonical, UriKind.Absolute, out var target) ||
            !target.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            !NormalizeCanonicalHost(target.Host).Equals(
                NormalizeCanonicalHost(pageUrl.Host),
                StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(target.Fragment))
        {
            return false;
        }

        var targetPath = target.AbsolutePath.TrimEnd('/');
        var pagePath = pageUrl.AbsolutePath.TrimEnd('/');
        if (!targetPath.Equals(pagePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !target.Query.Split(
                ['?', '&'],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Any(key =>
                key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("gclid", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("fbclid", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeCanonicalHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;

    private static void AddMetadataLengthObservation(
        string key,
        string label,
        Uri url,
        string value,
        int minimumLength,
        int maximumLength,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues,
        WebsiteHealthRecommendation recommendation)
    {
        var length = value.Length;
        var healthy = IsMetadataLengthHealthy(
            value,
            minimumLength,
            maximumLength);
        observations.Add(new Observation(
            key,
            label,
            "Content",
            healthy
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            (decimal)length,
            "characters",
            url.ToString(),
            healthy
                ? $"{length} characters."
                : $"{length} characters; recommended range is " +
                    $"{minimumLength}–{maximumLength}."));
        if (healthy)
        {
            return;
        }

        issues.Add(new DetectedIssue(
            key,
            $"{label} needs improvement",
            $"The current value is {length} characters; the recommended " +
                $"range is {minimumLength}–{maximumLength}.",
            url.ToString(),
            WebsiteHealthIssueSeverity.Warning,
            recommendation));
    }

    private static void AddDuplicateMetadataIssues(
        IReadOnlyCollection<PageSnapshot> pages,
        string key,
        string label,
        Func<PageSnapshot, string?> valueSelector,
        Func<PageSnapshot, Uri, Uri, WebsiteHealthRecommendation>
            recommendationFactory,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        var candidates = pages
            .Where(page =>
                !page.IsNoIndex &&
                !string.IsNullOrWhiteSpace(valueSelector(page)))
            .GroupBy(
                page => NormalizeMetadataTarget(page.Url),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(page => page.Url.Query.Length)
                .First())
            .ToList();
        var duplicateOf = new Dictionary<string, Uri>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var valueGroup in candidates
            .GroupBy(
                page => NormalizeComparableMetadata(valueSelector(page)),
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            var ordered = valueGroup
                .OrderBy(page => page.Url.AbsolutePath.Length)
                .ThenBy(page => page.Url.PathAndQuery)
                .ToList();
            var keeper = new Uri(NormalizeMetadataTarget(ordered[0].Url));
            foreach (var duplicate in ordered.Skip(1))
            {
                duplicateOf[NormalizeMetadataTarget(duplicate.Url)] = keeper;
            }
        }

        foreach (var page in candidates)
        {
            var targetValue = NormalizeMetadataTarget(page.Url);
            var target = new Uri(targetValue);
            var isDuplicate = duplicateOf.TryGetValue(
                targetValue,
                out var matchingUrl);
            observations.Add(new Observation(
                key,
                label,
                "Content",
                isDuplicate
                    ? WebsiteHealthCheckStatus.Warning
                    : WebsiteHealthCheckStatus.Passed,
                isDuplicate ? 1m : 0m,
                "duplicate",
                target.ToString(),
                isDuplicate
                    ? $"Matches {matchingUrl!.PathAndQuery}."
                    : "Unique across the bounded crawl."));
            if (!isDuplicate)
            {
                continue;
            }

            issues.Add(new DetectedIssue(
                key,
                label,
                $"This value exactly matches the value on " +
                    $"{matchingUrl!.PathAndQuery}.",
                target.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                recommendationFactory(page, target, matchingUrl)));
        }
    }

    internal static bool IsMetadataLengthHealthy(
        string? value,
        int minimumLength,
        int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= minimumLength &&
        value.Length <= maximumLength;

    internal static string NormalizeComparableMetadata(string? value) =>
        string.Join(
            ' ',
            WebUtility.HtmlDecode(value ?? "")
                .Split(
                    [' ', '\r', '\n', '\t'],
                    StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static void AddGroupedMetaDescriptionIssues(
        IReadOnlyCollection<PageSnapshot> pages,
        ICollection<DetectedIssue> issues)
    {
        var missingPages = pages.Where(page =>
            !page.IsNoIndex &&
            string.IsNullOrWhiteSpace(page.MetaDescription));
        foreach (var pageGroup in missingPages.GroupBy(
            page => NormalizeMetadataTarget(page.Url),
            StringComparer.OrdinalIgnoreCase))
        {
            var first = pageGroup
                .OrderBy(page => page.Url.Query.Length)
                .First();
            var target = new Uri(pageGroup.Key);
            var pageCount = pageGroup.Count();
            var isUtility =
                WebsiteHealthRecommendationBuilder.IsUtilityPage(target);
            issues.Add(new DetectedIssue(
                "meta-description",
                isUtility
                    ? "Utility page should be excluded from search"
                    : "Missing meta description",
                pageCount == 1
                    ? "A meta description was not detected on this page."
                    : $"A meta description was not detected across " +
                        $"{pageCount} crawled pagination variants. " +
                        "Make one change to the base collection.",
                target.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.MissingMetaDescription(
                    target,
                    first.Title,
                    first.Heading,
                    first.IntroductoryText)));
        }
    }

    internal static string NormalizeMetadataTarget(Uri url)
    {
        if (url.AbsolutePath.StartsWith(
                "/collections/",
                StringComparison.OrdinalIgnoreCase) &&
            url.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.TrimStart('?').StartsWith(
                    "page=",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return url.GetLeftPart(UriPartial.Path);
        }

        return url.ToString();
    }

    private static void AddGroupedImageAltIssues(
        IReadOnlyCollection<PageSnapshot> pages,
        ICollection<DetectedIssue> issues)
    {
        var occurrences = pages
            .SelectMany(page => page.MissingImages.Select(image => new
            {
                Page = page,
                Image = image,
                AssetKey = NormalizeImageAssetUrl(
                    page.Url,
                    image.Source) ?? $"{page.Url}|missing-source"
            }));
        foreach (var assetGroup in occurrences.GroupBy(
            item => item.AssetKey,
            StringComparer.OrdinalIgnoreCase))
        {
            var first = assetGroup.First();
            var affectedPages = assetGroup
                .Select(item => item.Page.Url.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(url => url)
                .ToList();
            var affectedUrl = NormalizeImageAssetUrl(
                    first.Page.Url,
                    first.Image.Source) ??
                first.Page.Url.ToString();
            var pageSummary = string.Join(
                ", ",
                affectedPages
                    .Take(3)
                    .Select(url => new Uri(url).PathAndQuery));
            if (affectedPages.Count > 3)
            {
                pageSummary +=
                    $" and {affectedPages.Count - 3} more page(s)";
            }

            issues.Add(new DetectedIssue(
                "image-alt",
                "Image is missing alt text",
                $"{assetGroup.Count()} occurrence(s) across " +
                $"{affectedPages.Count} page(s): {pageSummary}.",
                affectedUrl,
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.MissingImageAltText(
                    first.Page.Url,
                    first.Page.Title,
                    first.Page.Heading,
                    assetGroup.Select(item => item.Image).ToList())));
        }
    }

    private static void AddImageAltQualityIssues(
        IReadOnlyCollection<PageSnapshot> pages,
        ICollection<DetectedIssue> issues)
    {
        var occurrences = pages
            .SelectMany(page => page.Images.Select(image => new
            {
                Page = page,
                Image = image,
                AssetKey = NormalizeImageAssetKey(
                    page.Url,
                    image.Source) ?? $"{page.Url}|{image.Source}",
                NormalizedAlt = NormalizeAltText(image.AltText)
            }))
            .ToList();
        foreach (var assetGroup in occurrences
            .Where(item => IsGenericImageAlt(
                item.Image.AltText,
                item.Image.Source))
            .GroupBy(
                item => item.AssetKey,
                StringComparer.OrdinalIgnoreCase))
        {
            var first = assetGroup.First();
            var affectedPages = assetGroup
                .Select(item => item.Page.Url.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            issues.Add(new DetectedIssue(
                "image-alt",
                "Image alt text is not descriptive",
                $"The image uses generic alt text: " +
                    $"\"{first.Image.AltText}\" across " +
                    $"{affectedPages.Count} page(s).",
                NormalizeImageAssetUrl(
                    first.Page.Url,
                    first.Image.Source) ??
                    first.Page.Url.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.ImageAltQuality(
                    first.Page.Url,
                    first.Page.Title,
                    first.Page.Heading,
                    first.Image.AltText,
                    assetGroup.Select(item => item.Image).ToList(),
                    false)));
        }

        foreach (var altGroup in occurrences
            .Where(item =>
                !IsGenericImageAlt(
                    item.Image.AltText,
                    item.Image.Source) &&
                !item.Image.IsBrandLogo &&
                !IsReusableBrandAlt(item.NormalizedAlt))
            .GroupBy(
                item => item.NormalizedAlt,
                StringComparer.OrdinalIgnoreCase)
            .Where(group =>
                group.Select(item => item.AssetKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() >= 3))
        {
            var distinctAssets = altGroup
                .GroupBy(
                    item => item.AssetKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var first = distinctAssets[0];
            var pageCount = distinctAssets
                .Select(item => item.Page.Url.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            issues.Add(new DetectedIssue(
                "image-alt",
                "Alt text is reused across different images",
                $"\"{first.Image.AltText}\" is used for " +
                    $"{distinctAssets.Count} different image files across " +
                    $"{pageCount} page(s).",
                NormalizeImageAssetUrl(
                    first.Page.Url,
                    first.Image.Source) ??
                    first.Page.Url.ToString(),
                WebsiteHealthIssueSeverity.Warning,
                WebsiteHealthRecommendationBuilder.ImageAltQuality(
                    first.Page.Url,
                    first.Page.Title,
                    first.Page.Heading,
                    first.Image.AltText,
                    distinctAssets
                        .Select(item => item.Image)
                        .ToList(),
                    true)));
        }
    }

    internal static bool IsGenericImageAlt(
        string? altText,
        string? source)
    {
        var normalized = NormalizeAltText(altText);
        if (normalized.Length < 3 ||
            normalized is
                "image" or
                "photo" or
                "picture" or
                "thumbnail" or
                "product image" or
                "product photo" or
                "icon" or
                "logo" ||
            normalized.Contains(
                "untitled design",
                StringComparison.Ordinal) ||
            Regex.IsMatch(
                normalized,
                @"^(img|dsc|image)[\s_-]*\d+"))
        {
            return true;
        }

        var filename = Path.GetFileNameWithoutExtension(
            Uri.TryCreate(source, UriKind.Absolute, out var sourceUri)
                ? sourceUri.AbsolutePath
                : source ?? "");
        return filename.Length >= 3 &&
            NormalizeAltText(filename).Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAltText(string? value) =>
        string.Join(
            ' ',
            WebUtility.HtmlDecode(value ?? "")
                .Split(
                    [' ', '\r', '\n', '\t', '_', '-'],
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim()
            .ToLowerInvariant();

    private static bool IsReusableBrandAlt(string normalizedAlt) =>
        normalizedAlt.Contains(
            "green hills supply",
            StringComparison.Ordinal) &&
        normalizedAlt.Contains(
            "logo",
            StringComparison.Ordinal);

    internal static string? NormalizeImageAssetUrl(
        Uri pageUrl,
        string? source)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            !Uri.TryCreate(pageUrl, source, out var resolved) ||
            resolved.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return resolved.GetLeftPart(UriPartial.Path);
    }

    internal static string? NormalizeImageAssetKey(
        Uri pageUrl,
        string? source)
    {
        var normalizedUrl = NormalizeImageAssetUrl(pageUrl, source);
        if (normalizedUrl is null ||
            !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var resolved))
        {
            return null;
        }

        if (resolved.AbsolutePath.Contains(
                "/cdn/shop/",
                StringComparison.OrdinalIgnoreCase))
        {
            var filename = Path.GetFileName(resolved.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(filename))
            {
                return $"shopify:{filename.ToLowerInvariant()}";
            }
        }

        return normalizedUrl;
    }

    private static void AddPresenceObservation(
        string key,
        string label,
        string category,
        Uri url,
        bool present,
        string issueTitle,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues,
        WebsiteHealthIssueSeverity severity = WebsiteHealthIssueSeverity.Warning,
        WebsiteHealthRecommendation? recommendation = null,
        bool createIssue = true)
    {
        observations.Add(new Observation(
            key,
            label,
            category,
            present
                ? WebsiteHealthCheckStatus.Passed
                : WebsiteHealthCheckStatus.Warning,
            present ? 1m : 0m,
            "present",
            url.ToString(),
            present ? "Detected." : issueTitle));
        if (!present && createIssue)
        {
            issues.Add(new DetectedIssue(
                key,
                issueTitle,
                $"{label} was not detected on this page.",
                url.ToString(),
                severity,
                recommendation));
        }
    }

    private static void AddFailure(
        string key,
        string label,
        string category,
        Uri target,
        string detail,
        WebsiteHealthIssueSeverity severity,
        ICollection<Observation> observations,
        ICollection<DetectedIssue> issues)
    {
        observations.Add(new Observation(
            key,
            label,
            category,
            WebsiteHealthCheckStatus.Failed,
            null,
            null,
            target.ToString(),
            detail));
        issues.Add(new DetectedIssue(
            key,
            $"{label} check failed",
            detail,
            target.ToString(),
            severity,
            WebsiteHealthRecommendationBuilder.AvailabilityFailure(
                key,
                target)));
    }

    private static void ApplyScores(
        WebsiteCheckRun run,
        IReadOnlyCollection<Observation> observations)
    {
        run.AvailabilityScore = CategoryScore(observations, "Availability");
        run.SecurityScore = CategoryScore(observations, "Security");
        run.DiscoverabilityScore = CategoryScore(observations, "Discoverability");
        run.ContentScore = CategoryScore(observations, "Content");
        run.OverallScore = Math.Round(
            run.AvailabilityScore * .35m +
            run.SecurityScore * .20m +
            run.DiscoverabilityScore * .25m +
            run.ContentScore * .20m,
            1);
    }

    private static decimal CategoryScore(
        IEnumerable<Observation> observations,
        string category)
    {
        var categoryItems = observations
            .Where(item => item.Category == category)
            .ToList();
        if (categoryItems.Count == 0)
        {
            return 0;
        }

        return Math.Round(
            categoryItems.Average(item => item.Status switch
            {
                WebsiteHealthCheckStatus.Passed => 100m,
                WebsiteHealthCheckStatus.Warning => 60m,
                _ => 0m
            }),
            1);
    }

    private static void AddMetrics(
        ApplicationDbContext dbContext,
        MonitoredSite site,
        WebsiteCheckRun run,
        IEnumerable<Observation> observations)
    {
        var checkIds = site.Checks
            .GroupBy(check => check.Key)
            .ToDictionary(
                group => group.Key,
                group => group.First().Id,
                StringComparer.OrdinalIgnoreCase);
        foreach (var observation in observations)
        {
            dbContext.WebsiteHealthMetrics.Add(new WebsiteHealthMetric
            {
                WebsiteCheckRunId = run.Id,
                WebsiteCheckId = checkIds.GetValueOrDefault(observation.Key),
                Key = observation.Key,
                Label = observation.Label,
                Category = observation.Category,
                Status = observation.Status,
                NumericValue = observation.NumericValue,
                Unit = observation.Unit,
                AffectedUrl = Truncate(observation.AffectedUrl, 1000),
                Detail = Truncate(observation.Detail, 2000)
            });
        }
    }

    private static async Task SynchronizeIssuesAsync(
        ApplicationDbContext dbContext,
        Guid siteId,
        Guid runId,
        IEnumerable<DetectedIssue> detectedIssues,
        DateTime detectedAtUtc,
        CancellationToken cancellationToken)
    {
        var knownIssues = await dbContext.WebsiteHealthIssues
            .Where(issue => issue.MonitoredSiteId == siteId)
            .ToListAsync(cancellationToken);
        var seenFingerprints = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var detected in detectedIssues)
        {
            var fingerprint = CreateFingerprint(
                detected.CheckKey,
                detected.AffectedUrl);
            if (!seenFingerprints.Add(fingerprint))
            {
                continue;
            }

            var existing = knownIssues.FirstOrDefault(issue =>
                issue.Fingerprint == fingerprint);
            if (existing is null)
            {
                dbContext.WebsiteHealthIssues.Add(new WebsiteHealthIssue
                {
                    MonitoredSiteId = siteId,
                    Fingerprint = fingerprint,
                    CheckKey = detected.CheckKey,
                    Title = detected.Title,
                    Description = Truncate(detected.Description, 2000) ?? "",
                    AffectedUrl = Truncate(detected.AffectedUrl, 1000),
                    Recommendation = Truncate(
                        detected.Recommendation?.Guidance,
                        3000),
                    SuggestedValue = Truncate(
                        detected.Recommendation?.SuggestedValue,
                        6000),
                    CurrentValue = Truncate(
                        detected.Recommendation?.CurrentValue,
                        6000),
                    EvidenceJson = Truncate(
                        detected.Recommendation?.EvidenceJson,
                        16000),
                    FixLocation = Truncate(
                        detected.Recommendation?.FixLocation,
                        1000),
                    FixDocumentationUrl = Truncate(
                        detected.Recommendation?.DocumentationUrl,
                        1000),
                    Severity = detected.Severity,
                    FirstDetectedAtUtc = detectedAtUtc,
                    LastDetectedAtUtc = detectedAtUtc,
                    LastSeenRunId = runId
                });
            }
            else
            {
                existing.Title = detected.Title;
                existing.Description = Truncate(detected.Description, 2000) ?? "";
                existing.Recommendation = Truncate(
                    detected.Recommendation?.Guidance,
                    3000);
                existing.SuggestedValue = Truncate(
                    detected.Recommendation?.SuggestedValue,
                    6000);
                existing.CurrentValue = Truncate(
                    detected.Recommendation?.CurrentValue,
                    6000);
                existing.EvidenceJson = Truncate(
                    detected.Recommendation?.EvidenceJson,
                    16000);
                existing.FixLocation = Truncate(
                    detected.Recommendation?.FixLocation,
                    1000);
                existing.FixDocumentationUrl = Truncate(
                    detected.Recommendation?.DocumentationUrl,
                    1000);
                existing.Severity = detected.Severity;
                existing.LastDetectedAtUtc = detectedAtUtc;
                existing.LastSeenRunId = runId;
                existing.ResolvedAtUtc = null;
            }
        }

        foreach (var issue in knownIssues.Where(issue =>
            issue.ResolvedAtUtc == null &&
            !seenFingerprints.Contains(issue.Fingerprint)))
        {
            issue.ResolvedAtUtc = detectedAtUtc;
        }
    }

    private static string CreateFingerprint(string checkKey, string? url)
    {
        var input = $"{checkKey}|{url}".ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .ToLowerInvariant();
    }

    private static async Task DelayAsync(
        MonitoredSite site,
        CancellationToken cancellationToken)
    {
        if (site.RequestDelayMilliseconds > 0)
        {
            await Task.Delay(
                site.RequestDelayMilliseconds,
                cancellationToken);
        }
    }

    private static bool IsSameOrigin(Uri baseUri, Uri candidate) =>
        baseUri.Scheme == candidate.Scheme &&
        baseUri.Host.Equals(candidate.Host, StringComparison.OrdinalIgnoreCase) &&
        baseUri.Port == candidate.Port;

    internal static string NormalizeUrl(Uri uri) =>
        uri.GetComponents(
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.UriEscaped).TrimEnd('/');

    private static string? Truncate(string? value, int maximumLength) =>
        value is null || value.Length <= maximumLength
            ? value
            : value[..maximumLength];

    private sealed record FetchSnapshot(
        int? StatusCode,
        bool IsSuccess,
        bool IsHtml,
        double ResponseTimeMilliseconds,
        string Content,
        string? Error,
        Uri? FinalUri = null,
        int RedirectCount = 0,
        IReadOnlyDictionary<string, string>? Headers = null);

    private sealed record PageSnapshot(
        Uri Url,
        string? Title,
        string? Heading,
        int HeadingCount,
        string? IntroductoryText,
        string? MetaDescription,
        string? Canonical,
        StructuredDataAnalysis StructuredData,
        string? OpenGraphTitle,
        string? OpenGraphDescription,
        string? OpenGraphImage,
        string? OpenGraphUrl,
        string? TwitterCard,
        string? RobotsDirective,
        bool IsNoIndex,
        IReadOnlyList<WebsiteHealthMissingImage> MissingImages,
        IReadOnlyList<WebsiteHealthImage> Images,
        IReadOnlySet<Uri> ImageSources,
        IReadOnlySet<Uri> InternalLinks);

    private sealed record Observation(
        string Key,
        string Label,
        string Category,
        WebsiteHealthCheckStatus Status,
        decimal? NumericValue,
        string? Unit,
        string? AffectedUrl,
        string? Detail)
    {
        public Observation(
            string key,
            string label,
            string category,
            WebsiteHealthCheckStatus status,
            double numericValue,
            string? unit,
            string? affectedUrl,
            string? detail)
            : this(
                key,
                label,
                category,
                status,
                (decimal)numericValue,
                unit,
                affectedUrl,
                detail)
        {
        }
    }

    private sealed record DetectedIssue(
        string CheckKey,
        string Title,
        string Description,
        string? AffectedUrl,
        WebsiteHealthIssueSeverity Severity,
        WebsiteHealthRecommendation? Recommendation = null);
}
