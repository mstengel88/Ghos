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

Image accessibility checks distinguish a missing alt attribute from an
intentional `alt=""`. Empty alt text is accepted for decorative images and for
images inside links or buttons that already have an accessible name. Genuine
failures for a shared image asset are grouped into one finding across affected
pages.

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
