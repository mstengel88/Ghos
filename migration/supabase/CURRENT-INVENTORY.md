# Current managed Supabase inventory

Inventory date: 2026-07-27

This document combines local source inspection with read-only Supabase
Management API metadata. No production rows, credentials, API keys, JWTs, or
secret values were retrieved.

## Managed projects

All six accessible projects are healthy and currently run PostgreSQL 17.

| Managed project | Project ref | Region | Local source coverage | Deployed Edge Functions |
|---|---|---|---|---:|
| Ticket Printer | `dlayrpnmfnbjlxgnkczv` | us-west-2 | `/Users/mattstengel/edit-my-ticket` | 17 |
| WinterWatch-Pro | `caegybyfdkmgjrygnavg` | us-east-1 | `/Users/mattstengel/winterwatch` | 7 |
| Help Desk | `kryirjstfeksxotyabis` | us-west-2 | retired; no active app migration | 1 |
| GreenHills Quote Live | `dbyxbgbkokcddgeybjmf` | us-west-2 | `/Users/mattstengel/local-contractor` confirmed as primary source candidate by live schema | 0 |
| Local-Delivery | `mtntrlbuhcbdrngiubdu` | us-west-2 | `/Users/mattstengel/shipcalc2` and `/Users/mattstengel/local-delivery/dispatch-v2-sandbox` | 2 |
| Dump Site | `bnethnlrhwcjgjgjvoxz` | us-west-2 | `/Users/mattstengel/Documents/GreenHills APP/supabase` | 2 |

The stated five critical applications do not map one-to-one to these projects:
Dispatch and ShipCalc share Local-Delivery, while Help Desk is no longer used
and is excluded from active application cutover. Its managed project remains
untouched pending a separate archive/retention decision. Dispatch V2 Sandbox
is the sole continuing dispatch application. The older dispatch implementation
embedded in `local-contractor` will be retired after its unique production data
has been reconciled into the V2-owned model. GreenHills Quote Live remains in scope
because its quote tool and production data continue to matter.

## Deployed Edge Functions

### Ticket Printer

- `loadrite`
- `send-ticket-email`
- `send-report-email`
- `agent-action`
- `agent-stream`
- `agent-status`
- `agent-container-restart`
- `agent-containers`
- `agent-logs-stream`
- `agent-metrics`
- `agent-proxy`
- `agent-logs`
- `agent-registry`
- `address-autocomplete`
- `delete-account`
- `send-order-delivered-email`
- `loadrite-sync`

### WinterWatch-Pro

- `send-notification`
- `check-overtime`
- `get-weather`
- `overtime-action`
- `export-to-drive`
- `notify-maintenance-request`
- `home-assistant`

### Help Desk

- `send-ticket-email`

### Local-Delivery

- `shopify-api`
- `carrier-service`

### Dump Site

- `dump-site-api`
- `dump-site-bridge`

GreenHills Quote Live currently has no deployed Edge Functions. A project-scoped
read-only MCP inventory confirmed that it is nevertheless an active production
database containing the quote tool and populated dispatch data.

All currently listed deployed functions report `verify_jwt: false`. This does
not automatically mean they are unauthenticated—some inspect shared secrets or
credentials inside their handlers—but every function needs an explicit
authorization test before self-hosting.

## Deployed source comparison

The currently deployed function bundles were downloaded through the read-only
Management API and compared with the local repositories:

- Ticket Printer: deployed `loadrite-sync` differs from local source.
- WinterWatch-Pro: deployed `home-assistant` differs only in TypeScript result
  typing/casts; its queries and response behavior match the typed local source.
- Local-Delivery: deployed source matches; local `carrier-service` additionally
  contains a test file.
- Dump Site: deployed source matches local source.
- Help Desk: deployed function was recovered for archival evidence. No active
  application migration is planned.

Deployed bundles are retained under the ignored `exports/` directory. The two
drifts must be reconciled before declaring the local repositories authoritative.

## External secret dependencies

Only secret names were inventoried.

- Ticket Printer: Agent endpoints/secrets, Google Maps, Loadrite, Resend, and
  Supabase service credentials.
- WinterWatch-Pro: OneSignal, Lovable, and Supabase service credentials.
- Help Desk: SMTP, Resend, and Supabase service credentials.
- Local-Delivery: Shopify Admin/Storefront/API credentials, Google Maps,
  application admin password, Lovable, and Supabase service credentials.
- Dump Site: Shopify, Modern Retail, Resend, QR/bridge secrets, notification
  addresses, and Supabase service credentials.
- GreenHills Quote Live: no Edge Function secrets are currently registered.
  Shopify session/configuration data and application service-role usage still
  require a separate credential migration plan.

## Static schema findings

- Ticket Printer: 39 local migrations and extensive RLS/function history.
  Thirty-eight application migrations now pass in a disposable PostgreSQL 17
  database, producing 12 RLS-enabled tables, 53 policies, eight functions, and
  five triggers. Its Supabase-specific `pg_cron` migration is being replaced
  by a GHOS systemd timer. All 17 deployed Edge Functions pass secret-free
  local acceptance; the single deployed/local `loadrite-sync` drift is retained
  as a tracked baseline. See `projects/ticket-printer.md`.
- WinterWatch-Pro: 36 local migrations. Thirty-four portable application
  migrations now pass in disposable PostgreSQL 17, producing 19 RLS-enabled
  tables, 70 policies, 47 functions, and 18 triggers. Its two managed
  `pg_cron`/`pg_net` migrations are replaced by a GHOS systemd timer.
  Auth, private Storage, Realtime, seven Edge Functions, and external services
  remain separate migration gates. All seven Edge Functions pass secret-free
  local acceptance. Its browser/PWA client is environment-switchable on
  migration branch `codex/self-hosted-supabase-config`. See
  `projects/winterwatch-pro.md`.
- Local-Delivery/ShipCalc: four local migrations in `shipcalc2`; Dispatch V2
  Sandbox is the canonical dispatch application and must own the final dispatch
  migration baseline. Shared dispatch tables in GreenHills Quote Live are a
  one-time reconciliation source, not a second continuing implementation.
  Read-only MCP inventory confirmed 22 public tables and 13 Auth users. The
  2026-07-28 follow-up recorded 966 dispatch orders, 588 stop-metric records,
  and 470 Storage objects. See
  `projects/local-delivery.md`.
- GreenHills Quote Live: 22 public tables, 11 Auth users, 89 quotes, 457
  dispatch orders, 20 visible public RLS policies, and a public but empty
  `dispatch-photos` bucket. See
  `projects/greenhills-quote-live.md` for the MCP-backed inventory.
- Dump Site: eight migrations, `pgcrypto`, queue/claim RPC functions, and two
  Edge Functions. Its migrations and core queue workflow now pass in a
  disposable PostgreSQL 17 database. Both Edge Functions also pass local
  authorization-contract acceptance with test-only credentials, and the iOS
  and Android managed-URL cutover points are documented; see
  `projects/dump-site.md`.
- Older `/Users/mattstengel/build-my-app` and `build-my-app2` folders are
  WinterWatch-Pro predecessors, not additional managed projects.

## Known migration risks

1. Managed databases run PostgreSQL 17, so compatibility testing must also use
   PostgreSQL 17.
2. Auth, Storage objects, Edge Functions, secret values, cron jobs, and external
   callbacks require separate migration work beyond a database dump.
3. Existing managed JWTs will not remain valid after switching to newly
   generated self-hosted signing keys; clients must re-authenticate.
4. Public Shopify and email/OAuth callbacks cannot rely on Tailscale-only URLs.
5. Several application schemas reuse generic table names. Projects remain
   isolated during the infrastructure migration.
6. Help Desk is retired and does not block GHSSERVER application cutover. Its
   managed project still requires a retention/archive decision before any
   scale-down or deletion.
7. GreenHills Quote Live is bound to `local-contractor` for the quote tool, but
   its legacy dispatch schema and rows must be reconciled into the
   Local-Delivery/Dispatch V2 model before the old dispatch runtime is disabled.
   No legacy dispatch table or data should be deleted until row counts,
   relationships, history, photos, and active work have been validated.
8. Local-Delivery contains about 611.1 MiB across 470 dispatch-photo objects.
   Of these, 452 match current order photo references and 18 are unreferenced.
   Storage
   must be migrated with object metadata and order references, not treated as a
   disposable cache.
