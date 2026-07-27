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
| Help Desk | `kryirjstfeksxotyabis` | us-west-2 | canonical source not yet located | 1 |
| GreenHills Quote Live | `dbyxbgbkokcddgeybjmf` | us-west-2 | likely quote-tool source; project binding not yet confirmed | 0 |
| Local-Delivery | `mtntrlbuhcbdrngiubdu` | us-west-2 | `/Users/mattstengel/shipcalc2` and `/Users/mattstengel/local-delivery/dispatch-v2-sandbox` | 2 |
| Dump Site | `bnethnlrhwcjgjgjvoxz` | us-west-2 | `/Users/mattstengel/Documents/GreenHills APP/supabase` | 2 |

The stated five critical applications do not map one-to-one to these projects:
Dispatch and ShipCalc share Local-Delivery, while Help Desk and GreenHills Quote
Live currently lack a confirmed canonical source binding. All six projects stay
in scope until the owner confirms which one is noncritical or shared.

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

GreenHills Quote Live currently has no deployed Edge Functions.

All currently listed deployed functions report `verify_jwt: false`. This does
not automatically mean they are unauthenticated—some inspect shared secrets or
credentials inside their handlers—but every function needs an explicit
authorization test before self-hosting.

## Deployed source comparison

The currently deployed function bundles were downloaded through the read-only
Management API and compared with the local repositories:

- Ticket Printer: deployed `loadrite-sync` differs from local source.
- WinterWatch-Pro: deployed `home-assistant` differs from local source.
- Local-Delivery: deployed source matches; local `carrier-service` additionally
  contains a test file.
- Dump Site: deployed source matches local source.
- Help Desk: deployed function was recovered, but canonical application source
  is still missing.

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

## Static schema findings

- Ticket Printer: 39 local migrations and extensive RLS/function history.
- WinterWatch-Pro: 36 local migrations, Auth, Storage, Realtime, `pg_cron`, and
  `pg_net`.
- Local-Delivery/ShipCalc: four local migrations in `shipcalc2`; Dispatch V2
  references 14 shared tables that need a canonical migration baseline.
- Dump Site: eight migrations, `pgcrypto`, queue/claim RPC functions, and two
  Edge Functions.
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
6. Help Desk canonical source and GreenHills Quote Live source binding must be
   located before either project can pass the source-completeness gate.
