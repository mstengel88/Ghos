using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ghos.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Ghos.Web.WebsiteHealth;

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

            if (enabledCheckKeys.Contains("sitemap"))
            {
                var sitemapUri = new Uri(baseUri, "/sitemap.xml");
                await DelayAsync(site, cancellationToken);
                var sitemap = await FetchPageAsync(
                    sitemapUri,
                    site,
                    cancellationToken);
                checkedUrls.Add(NormalizeUrl(sitemapUri));
                AddResourceObservation(
                    "sitemap",
                    "Sitemap",
                    "Discoverability",
                    sitemapUri,
                    sitemap,
                    observations,
                    issues);
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
                if (snapshot.IsHtml && snapshot.IsSuccess)
                {
                    pages[NormalizeUrl(target)] =
                        await AnalyzePageAsync(target, snapshot.Content, cancellationToken);
                }
            }

            var crawlEnabled = enabledCheckKeys.Overlaps(
                [
                    "internal-link",
                    "title",
                    "title-length",
                    "duplicate-title",
                    "meta-description",
                    "meta-description-length",
                    "duplicate-meta-description",
                    "image-alt",
                    "canonical",
                    "schema"
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
                return new FetchSnapshot(
                    (int)response.StatusCode,
                    response.IsSuccessStatusCode,
                    mediaType.Contains(
                        "html",
                        StringComparison.OrdinalIgnoreCase),
                    stopwatch.Elapsed.TotalMilliseconds,
                    content,
                    null);
            }

            stopwatch.Stop();
            return new FetchSnapshot(
                null,
                false,
                false,
                stopwatch.Elapsed.TotalMilliseconds,
                string.Empty,
                "The URL exceeded the five-redirect safety limit.");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException)
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
                    : exception.Message);
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
                image.GetAttribute("title") ??
                    image.GetAttribute("aria-label") ??
                    image.ParentElement?.GetAttribute("aria-label") ??
                    image.ParentElement?.GetAttribute("title") ??
                    image.ParentElement?.TextContent,
                url.ToString()))
            .ToList();
        var introductoryText = document.QuerySelectorAll("main p, article p")
            .Select(element => element.TextContent?.Trim())
            .FirstOrDefault(text => text?.Length >= 50) ??
            document.QuerySelectorAll("p")
                .Select(element => element.TextContent?.Trim())
                .FirstOrDefault(text => text?.Length >= 50);

        return new PageSnapshot(
            url,
            document.Title?.Trim(),
            document.QuerySelector("h1")?.TextContent?.Trim(),
            introductoryText,
            document.QuerySelector("meta[name='description']")
                ?.GetAttribute("content")?.Trim(),
            document.QuerySelector("link[rel~='canonical']")
                ?.GetAttribute("href")?.Trim(),
            document.QuerySelectorAll("script[type='application/ld+json']").Length,
            document.QuerySelector("meta[name='robots']")
                ?.GetAttribute("content")
                ?.Contains(
                    "noindex",
                    StringComparison.OrdinalIgnoreCase) == true,
            missingImages,
            links);
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
                        page.Title.Length));
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
                        page.MetaDescription.Length));
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

            if (enabledCheckKeys.Contains("schema"))
            {
                AddPresenceObservation(
                    "schema",
                    "Structured data",
                    "Discoverability",
                    page.Url,
                    page.SchemaBlockCount > 0,
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

            if (enabledCheckKeys.Contains("image-alt"))
            {
                observations.Add(new Observation(
                    "image-alt",
                    "Image alt text",
                    "Content",
                    page.MissingImages.Count == 0
                        ? WebsiteHealthCheckStatus.Passed
                        : WebsiteHealthCheckStatus.Warning,
                    (decimal)page.MissingImages.Count,
                    "images",
                    page.Url.ToString(),
                    page.MissingImages.Count == 0
                        ? "All images have alt text."
                        : $"{page.MissingImages.Count} image(s) are missing alt text."));
            }
        }

        if (enabledCheckKeys.Contains("image-alt"))
        {
            AddGroupedImageAltIssues(pageSnapshots, issues);
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
        string? Error);

    private sealed record PageSnapshot(
        Uri Url,
        string? Title,
        string? Heading,
        string? IntroductoryText,
        string? MetaDescription,
        string? Canonical,
        int SchemaBlockCount,
        bool IsNoIndex,
        IReadOnlyList<WebsiteHealthMissingImage> MissingImages,
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
