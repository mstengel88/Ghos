# GHOS Website Health Center

The Website Health Center is a native GHOS module at `/website-health`. It
stores monitored sites, check definitions, runs, issues, and historical metrics
in the existing PostgreSQL database.

## Access

- Administrators, Managers, and Operations users can view the dashboard.
- Administrators and Managers can start a manual check, triage findings, and
  change monitoring settings.
- Only one check can run at a time in a GHOS instance.

## Operations

Open **Issue center** from the Website Health dashboard to filter findings by
status, check type, or affected URL. Administrators and Managers can
acknowledge findings, add a triage note, suppress accepted findings, restore
them, or reopen acknowledged work. Suppressed findings remain in history but
do not count toward the dashboard's active-issue total.

Each finding includes a recommended fix and, when applicable, copy-ready
suggested wording or markup. Suggestions use the live page heading,
introductory copy, URL structure, and image context. The monitor provides
specific titles, meta descriptions, image alt text, canonical tags, JSON-LD,
robots.txt content, and broken-link guidance. A later crawl refreshes the
recommendation when the source page changes.

The Issue Center also stores and displays the exact Shopify Admin surface for
each recommended change. Collection pagination variants are grouped into one
base-collection metadata finding, so a manager is not asked to edit pages 2,
3, and 5 independently.

Website Health stores timestamps in UTC and displays them in
**America/Chicago** Central time. The interface automatically shows CST or CDT
as daylight-saving time changes.

The content scan checks more than presence. It flags page titles outside the
20–60 character range and uses a soft 70–170 character range for meta
descriptions. Google has no fixed description length limit, so the soft ceiling
avoids treating a useful description as defective for exceeding a display
heuristic by one or two characters.
It also detects exact duplicate titles or descriptions across indexable pages in the
bounded crawl. Duplicate comparison ignores capitalization, encoded HTML
characters, and repeated whitespace. Paginated collection URLs are treated as
one metadata target to prevent duplicate work. Each finding includes unique
replacement wording and the correct Shopify editing surface.

Discoverability checks also inspect the contents of `robots.txt` and
`sitemap.xml`. GHOS verifies that the wildcard crawler group does not block the
entire storefront, that the production sitemap is declared, that sitemap XML
uses a supported sitemap root, and that every listed location is an absolute
HTTPS URL on the production domain. Recommendations preserve Shopify's default
crawler protections and never tell a manager to hand-author a static Shopify
sitemap.

Redirect checks record the final destination and hop count for each crawled
page. A single `www` to canonical-domain redirect is treated as healthy when it
keeps the same HTTPS path. Multi-hop chains, cross-domain destinations, and
single redirects to another path are reported with the final replacement URL
and Shopify navigation/URL-redirect locations.

The homepage security-header check verifies active HSTS, MIME-sniffing
protection, framing protection through X-Frame-Options or CSP, and the presence
of a Content Security Policy. Findings point to Shopify domain status and
Cloudflare response-header rules because these protections are delivered by the
platform/CDN; GHOS never recommends pasting HTTP headers into Liquid markup.

Product structured-data validation goes beyond detecting an `@type` value.
For every crawled product page, GHOS checks the Product name, image, page URL,
offers, price, ISO currency, and availability. Repair guidance requires live
Shopify product and variant values so price and inventory markup cannot drift
from what customers can actually purchase.

Within the bounded crawl, customer-facing product links are queued before
collection, page, blog, and utility links. This ensures a 25-page run validates
representative commerce pages instead of exhausting its limit on navigation
and policy routes.

The crawler also uses Shopify's generated sitemap index as a discovery
inventory. It reads no more than eight same-domain product, collection, page,
or blog sitemap files and no more than 5,000 listed locations. Agent-discovery
and external sitemap files are excluded. Sitemap URLs are normalized to the
configured storefront host and still must pass the HTTPS, public-address,
robots.txt, timeout, delay, and maximum-page safeguards before GHOS requests
them. Within each content category, pages that have never been evaluated are
queued before recently checked pages, allowing recurring 25-page runs to
expand coverage over time without turning one run into an unbounded crawl.

Each completed run also persists the eligible sitemap inventory and rolling
seven-day crawl coverage. The dashboard reports both the percentage and exact
page count, such as `100 of 151 sitemap pages were evaluated in the last 7
days`. Coverage includes pages checked during the current run and pages
evaluated by earlier runs inside the rolling window. It is operational
visibility, not a pass/fail SEO signal, so it does not raise or lower the
website health score.

The coverage panel breaks the rolling total into products, collections, pages,
and blog content. Each page type stores its own percentage and exact evaluated
and inventory counts. A category with low coverage simply indicates where the
safe rotating crawl will work next; it does not create an issue or reduce the
health score.

For an overlong meta description, GHOS condenses the page's current live copy
instead of replacing it with a generic category template. The result preserves
page-specific products, applications, and benefits, decodes HTML entities,
ends on a word boundary, and remains within 155 characters. Short descriptions
retain useful existing wording and add page-specific context.

Metadata-quality findings store and display the current live value next to the
suggested replacement, with character counts for both. This comparison is
historical issue data, so managers can review exactly what the scanner observed
before copying a recommendation into Shopify.

The social-preview scan checks indexable customer pages for an Open Graph
title, description, image, canonical sharing URL, and Twitter/X card type.
Incomplete previews include the current live values, page-specific replacement
text, and a Shopify route based on whether the affected URL is a product,
collection, article, page, or the homepage. Theme guidance tells managers to
search the full theme for the rendered metadata snippet instead of assuming a
particular Liquid filename exists.

The **Download fix list** action exports the currently filtered Issue Center
view as a UTF-8 CSV. It includes current and suggested values, character counts,
status, severity, Shopify location, official documentation, and triage notes.
CSV cells are quoted and spreadsheet-formula prefixes are neutralized.

Blog-index metadata is managed through **Content → Blog posts → Manage blogs →
open the blog → Search engine listing preview**, not through a presumed Liquid
template. Individual blog posts continue to point to their own search engine
listing editor.

Shopify's `/collections/all` route is a built-in catalog page. GHOS never
recommends renaming an existing collection to take over that handle, because a
theme section can reference the existing handle and disappear when it changes.
The catalog recommendation first checks for an existing editable **All**
collection. Green Hills Supply uses Shopify's new Collections experience, which
replaces separate Smart and Manual types with sources. When no editable
collection owns `/all`, GHOS directs the manager to **Add collection**, use the
default **Products** source, and select **Add condition** rather than manually
adding products. Stores that do not use the catalog route can remove it from
monitored key pages or suppress the finding.

Supported Shopify remediation paths include a direct **Official Shopify
instructions** link in the Issue Center. Theme-dependent or custom-code
recommendations do not receive a documentation link.

Administrators and Managers can copy a suggested value directly from the
finding and select **Verify fix** after publishing the Shopify change. GHOS
runs a fresh live check and reports whether that specific issue resolved or is
still present. Verification uses the same single-run coordinator, timeouts,
rate limits, and crawl boundaries as scheduled monitoring.

A finding is resolved only after a later run evaluates the same affected page
or image and no longer detects the problem. Findings from pages outside a
later bounded crawl remain open instead of being incorrectly cleared merely
because that run sampled different URLs.

Image accessibility checks distinguish a missing alt attribute from an
intentional `alt=""`. Empty alt text is accepted for decorative images and for
images inside links or buttons that already have an accessible name. Genuine
failures for a shared image asset are grouped into one finding across affected
pages.

For product galleries, interface labels such as **Gallery Viewer** are ignored
when generating alt-text recommendations. GHOS instead derives specific finish,
color, and variant wording from descriptive Shopify media filenames, including
camel-cased names, and resolves protocol-relative CDN URLs for direct evidence
links. Stock-library and generic filenames remain excluded.

Image reliability checks also request a bounded set of unique image assets
found during the crawl. Responsive size variants are grouped as one asset,
requests use the site's existing timeout and delay controls, and no more than
25 unique assets are checked in a run. Broken assets list every affected
crawled page and the appropriate Shopify product, collection, page, or theme
image location.

Open **Settings** to enable or pause a monitored site, change its recurring
interval and safe crawl limits, enable or disable individual checks, and manage
the key pages included in each run. Homepage availability is always enabled
because it is the foundation for the other checks.

## First startup

GHOS applies EF Core migrations automatically during startup. The initializer
then creates the first monitored site:

```text
https://www.greenhillssupply.com
```

It also creates the homepage, SSL, robots, sitemap, key-page, internal-link,
title, meta-description, image-alt, canonical, and structured-data check
definitions.

Start GHOS locally with its existing safe defaults:

```bash
docker compose up -d --build
```

The scheduler is disabled by default. Sign in as an Administrator or Manager,
open **Website Health**, and select **Run health check**.

## Enable recurring production checks

Use the checked-in Compose overlay to enable the hosted scheduler:

```bash
docker compose \
  -f compose.yml \
  -f compose.website-health.yml \
  up -d --build
```

Optional scheduler settings:

```bash
export WEBSITE_HEALTH_SCHEDULER_ENABLED=true
export WEBSITE_HEALTH_INITIAL_DELAY_SECONDS=120
export WEBSITE_HEALTH_SCHEDULER_POLL_MINUTES=5
```

Each monitored site has its own interval, timeout, delay, and crawl-page limit.
Green Hills Supply starts with a 60-minute interval, 15-second request timeout,
300 ms delay between requests, and a maximum of 25 crawled pages.

## Migration

The migrations are:

```text
20260803225113_AddWebsiteHealthCenter
20260803232802_AddWebsiteHealthIssueTriage
20260803234045_AddWebsiteHealthRecommendations
20260804003828_AddWebsiteHealthFixLocations
20260804011540_AddWebsiteHealthFixDocumentation
20260804014042_AddWebsiteHealthCurrentValues
20260804015436_AddWebsiteHealthReviewedValues
```

To apply it explicitly from a machine with the .NET 10 SDK:

```bash
dotnet ef database update \
  --project src/Ghos.Web/Ghos.Web.csproj
```

## Verification

Run the application tests:

```bash
dotnet test src/Ghos.Web.Tests/Ghos.Web.Tests.csproj
```

Build the production image:

```bash
docker build \
  -f src/Ghos.Web/Dockerfile \
  -t ghos-web:website-health \
  src/Ghos.Web
```

The crawler only follows public HTTPS targets on the default port, caps
redirects and response size, respects wildcard `robots.txt` disallow rules,
uses explicit timeouts, delays requests, limits concurrent connections, and
bounds each crawl.

HTTP 408, 425, 429, and selected 5xx responses are treated as transient rather
than broken links. For Shopify rate limits, GHOS retries the same request up to
two times, honors a short `Retry-After` delay when supplied, and caps each wait
at three seconds so one URL cannot stall the bounded run. An exhausted 429 is
recorded as a temporary warning and never produces instructions to remove or
redirect a valid storefront link.
