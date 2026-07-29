# Dump Site migration contract

Managed project: Dump Site (`bnethnlrhwcjgjgjvoxz`)

Canonical source:
`/Users/mattstengel/Documents/GreenHills APP/supabase`

Status: local PostgreSQL 17 schema, exact managed-schema comparison,
queue-workflow rehearsal, and both Edge Function authorization contracts pass.
Production rows, production secrets, client cutover, and external callbacks
are not migrated.

## Application contract

The source contains eight ordered migrations. Together they create:

- three service-only public tables with RLS enabled;
- 32 columns on `dump_site_entries`;
- two application triggers;
- nine Dump Site functions;
- generated order numbers beginning with `201-D10000`;
- idempotent client-submission handling;
- notification and optional Modern Retail status;
- the CounterPoint bridge queue, claim lease, completion, operator claim, and
  operator release workflows; and
- automatic queueing when Modern Retail is disabled.

The local rehearsal applies all eight migrations to a disposable database
inside the pinned Supabase PostgreSQL 17 container. It verifies table and
column counts, RLS, role grants, order-number generation, automatic queueing,
claim/completion behavior, and rate limiting. It also compares normalized
column, constraint, index, function, and trigger fingerprints with the
read-only live inventory. The disposable database is removed after every run.

The rehearsal pre-installs `pgcrypto` in the `extensions` schema, matching
managed Supabase. Without that bootstrap, a blank PostgreSQL database installs
the extension into `public`, making platform functions look like false
application-schema drift.

Run it with:

```bash
tools/verify_dump_site_schema.sh
```

## Edge Function compatibility

Both inventoried functions run in the isolated local Supabase Edge Runtime
using deterministic test-only credentials. The acceptance test verifies:

- the API POST-only and route-not-found guards;
- submission validation before any database access;
- negative and positive QR-token checks;
- missing and incorrect bridge-secret rejection;
- authenticated bridge payload validation; and
- authenticated bridge health.

The test cannot target a non-loopback URL, verifies both source hashes before
startup, does not invoke Shopify, Resend, Modern Retail, or production
Supabase, and restores the Local-Delivery functions when it exits.

Run it with:

```bash
tools/verify_dump_site_edge_functions.sh
```

## Client cutover inventory

The standalone clients still point directly at the managed function URL:

- iOS: `ProjectInfo.plist` key `DumpSiteAPIBaseURL`;
- Android: `GreenHillsINC-Android/app/build.gradle.kts` build configuration.

The iOS value is already configuration-shaped, but its release value still
needs to be supplied by the future environment/build pipeline. Android needs
the same environment-specific build configuration instead of a literal
managed-project URL. The canonical application checkout is highly modified, so
no client files were changed during this server-side rehearsal.

Cutover must update both clients to the same public HTTPS function gateway.
Tailscale-only access is not sufficient for the Shopify website/app-proxy
workflow. The managed URL remains the rollback target until mobile and website
acceptance passes.

The current clients can be checked without modifying them:

```bash
tools/verify_dump_site_client_config.sh
```

During staging/cutover, require an exact candidate endpoint in both clients:

```bash
DUMP_SITE_EXPECTED_API_BASE=https://candidate.example/functions/v1/dump-site-api \
  tools/verify_dump_site_client_config.sh
```

The check fails if iOS and Android disagree, the endpoint is not HTTPS, its
function path is malformed, or it does not equal the explicitly expected
candidate. With no expected value, it reports whether the synchronized clients
remain on managed Supabase or use a custom HTTPS candidate.

## Source fingerprints

| Source | SHA-256 |
|---|---|
| `20260723000000_dump_site.sql` | `5b98274bf79d1117219dd07dab708111de87d7425f416ea828b782dd4cbd2993` |
| `20260723010000_dump_site_email_notifications.sql` | `608655f4f1b6e3db594f4612382425179b696b0913121a05a0793ed8e2da8304` |
| `20260723020000_dump_site_modern_retail.sql` | `9da1d59ff017a835ad95f69f75e23767996f47e014f7801a28c0f5fedcf0a67a` |
| `20260723030000_dump_site_order_numbers.sql` | `de10ec59ad6b09ee7f291a14dc40b9b2a4c9562a2da9f1c2679d77334a6d2a0e` |
| `20260724000000_dump_site_counterpoint_bridge.sql` | `faeda4dbb8776b7fa5e787b8b07d026444d928d8317468edb718428584533679` |
| `20260724010000_dump_site_201_d_order_numbers.sql` | `b32f803d7725e6f1f8801c7b0735742c147e42f835b15acf5d30482666f294cb` |
| `20260724020000_dump_site_operator_queue.sql` | `c36e4807db341c8f8ad0296b4128251039354254ba8f6b3c229aaeedbb5237a0` |
| `20260724030000_disable_dump_site_modern_retail.sql` | `6afb17766444f1202724657964deb383e70846ecec7b0da331cbfa205805faf6` |
| `functions/dump-site-api/index.ts` | `4732a4a6e92a7bcfcb200a66d657d7fd2478f8892eafa1ee7c55f9057761d38b` |
| `functions/dump-site-bridge/index.ts` | `3bdf9a9b88cd195a0113d93f5c7e337bc3f4d25210139e87f05cb13dcdfcecf3` |

The two local Edge Function files match the deployed source captured through
the Supabase Management API. Managed versions were active at inventory time:
`dump-site-api` version 19 and `dump-site-bridge` version 5. Both report
`verify_jwt: false`; the API implements its own access checks, and the bridge
requires a shared bearer secret.

## Managed-project inventory

Read-only Supabase MCP inspection on 2026-07-28 confirmed:

- PostgreSQL 17.6.1.147;
- approximately 10.8 MB total database size;
- all eight managed migration-history entries exactly match local migration
  names and ordering;
- three public tables, all with RLS enabled;
- zero public policies, matching the service-only access model;
- nine public functions and two application triggers;
- zero Auth users, identities, and sessions;
- zero Storage buckets and objects;
- no table in the `supabase_realtime` publication; and
- the same two active Edge Functions already captured in source inventory.

No row payloads, credentials, secret values, or API keys were retrieved.

Aggregate application state:

| Object | Count |
|---|---:|
| `dump_site_sessions` | 17 |
| `dump_site_entries` | 6 |
| `dump_site_rate_limits` | 16 |

The six entries span 2026-07-24. Four are not requested for the CounterPoint
bridge and two are queued. Four retain the historical Modern Retail `sent`
state and two use the current `disabled` state. The two order-number sequence
positions were captured so restore validation can prevent duplicate numbers:

- `dump_site_order_number_seq`: last value `10001`;
- `dump_site_201_d_order_number_seq`: last value `10002`.

The normalized live fingerprints are now enforced by
`tools/verify_dump_site_schema.sh`. Columns, constraints, indexes, all nine
application functions, and both triggers match the local source.

The security advisor reports only the three intentional “RLS enabled, no
policy” notices. These tables are service-only, and browser roles have no table
privileges. The performance advisor reports unused indexes, which is expected
with six rows and is not sufficient reason to remove them.

## Secret-name inventory

Secret values were not retrieved. The deployed functions depend on:

- Supabase URL, public keys, service credentials, database URL, and JWKS;
- Shopify API credentials;
- Dump Site QR and bridge secrets;
- Resend and notification sender/recipient settings; and
- optional Modern Retail credentials, enablement, and item mapping.

Secrets must be entered into the future GHSSERVER runtime from the company
password manager. They must not be copied into Git, Docker images, migration
reports, or command history.

## Remaining gates

1. Capture an encrypted exact database export without resetting a production
   database password.
2. Restore the encrypted data export into the isolated Dump Site database and
   verify counts, constraints, and generated-number sequence state.
3. Capture the Edge Function secret values through an authorized private
   handoff and test Shopify, email, QR, and bridge callbacks using staging
   credentials. The local authorization contract already passes with test-only
   credentials.
4. Deploy behind HTTPS because the Shopify and QR workflows cannot use a
   Tailscale-only callback.
5. Run the GHOS queue and the standalone Dump Site client against the same
   candidate backend before the managed-project cutover.
6. Keep managed Supabase intact through the rollback observation window.
