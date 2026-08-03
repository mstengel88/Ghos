# GHOS Website Health Center

The Website Health Center is a native GHOS module at `/website-health`. It
stores monitored sites, check definitions, runs, issues, and historical metrics
in the existing PostgreSQL database.

## Access

- Administrators, Managers, and Operations users can view the dashboard.
- Administrators and Managers can start a manual check.
- Only one check can run at a time in a GHOS instance.

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

The migration is:

```text
20260803225113_AddWebsiteHealthCenter
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
