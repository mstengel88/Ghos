# Local-Delivery — managed project inventory

Inventory date: 2026-07-27

Follow-up data and Storage snapshot: 2026-07-28

Project ref: `mtntrlbuhcbdrngiubdu`

Inventory method: official Supabase MCP, project-scoped to Local-Delivery with
read-only database access. No production rows containing customer details,
credentials, tokens, or secret values were exported.

## Executive summary

Local-Delivery is the active production data source for Dispatch V2 Sandbox and
the canonical source for the future dispatch migration. It also contains the
shipping calculator and a newer copy of the quote/B2B data model.

- PostgreSQL: 17.6
- Database size: 356 MB
- Public tables: 22
- Public RLS policies: 26
- Public functions: 8
- Public triggers: 8
- Public indexes: 106
- Auth users: 13
- Storage buckets: 1
- Storage objects: 470
- Storage bytes: 640,756,931 (about 611.1 MiB)
- Supabase migration history entries: 4

All 22 public tables have RLS enabled. No public views or materialized views
were found.

## Exact public table counts

| Table | Rows |
|---|---:|
| `app_audit_log` | 866 |
| `app_settings` | 16 |
| `app_user_profiles` | 12 |
| `custom_delivery_quotes` | 210 |
| `dispatch_audit_log` | 38,066 |
| `dispatch_b2b_companies` | 82 |
| `dispatch_driver_locations` | 11 |
| `dispatch_employees` | 12 |
| `dispatch_notifications` | 45 |
| `dispatch_orders` | 966 |
| `dispatch_push_subscriptions` | 2 |
| `dispatch_routes` | 24 |
| `dispatch_settings` | 2 |
| `dispatch_shopify_updates` | 156 |
| `dispatch_stop_metrics` | 588 |
| `dispatch_trucks` | 8 |
| `dispatch_user_roles` | 11 |
| `origin_addresses` | 5 |
| `product_source_map` | 144 |
| `Session` | 1 |
| `shipping_material_rules` | 9 |
| `shopify_app_settings` | 1 |

At inventory time, dispatch order status was:

| Status | Rows |
|---|---:|
| `delivered` | 943 |
| `cancelled` | 12 |
| `new` | 9 |

The order history spans April 25 through July 27, 2026. The newest audit event
was recorded during this inventory window, confirming that the project is
actively receiving writes.

## Installed extensions

- `pg_stat_statements` 1.11
- `pg_trgm` 1.6
- `pgcrypto` 1.3
- `plpgsql` 1.0
- `supabase_vault` 0.3.1
- `uuid-ossp` 1.1

The PostgreSQL 17 compatibility target supports the application-required
extensions, but Supabase platform schemas still require the full self-hosted
stack rather than a plain PostgreSQL restore.

## Auth and Storage

- `auth.users` contains 13 users.
- `app_user_profiles` contains 12 profiles.
- `dispatch_user_roles` contains 11 role records.
- The public `dispatch-photos` bucket contains 470 objects.
- The bucket holds about 611.1 MiB.
- 452 objects match current order photo references; 18 are unreferenced.
- Its configured object-size limit is 10 MiB.
- Allowed types are JPEG, PNG, WebP, HEIC, and HEIF.

The difference between Auth, profile, and dispatch-role counts must be
reconciled by user ID before cutover. Do not infer that the unmatched accounts
are disposable.

Storage is a material part of this migration. The bucket metadata, policies,
object paths, object bytes, metadata, and any order references must be exported
and restored together.

The read-only migration tooling now includes:

- `tools/export_local_delivery_database.sh`, which uses the Supabase CLI token
  from macOS Keychain, the exact shared-pooler endpoint supplied by
  `supabase link`, pinned Supabase dump scripts, live reconciliation counts,
  SHA-256 verification, and AES-256 encryption;
- `tools/configure_local_delivery_storage_export.sh`, which copies the existing
  server-only Storage credential from the canonical Dispatch V2 environment
  into the Git-ignored migration secret store without printing it;
- `tools/export_local_delivery_storage.sh`, which validates the project URL and
  service-role credential before privately downloading `dispatch-photos` with
  resumable object and SHA-256 manifests.

The database exporter never needs the long-lived database password. It uses
Supabase temporary database access, which must be enabled for this project and
must map the operator to the `postgres` role. The exporter fails closed when
that mapping is absent; it does not reset credentials or alter production data.

Temporary access was enabled for the current operator on 2026-07-29. The
read-only exporter then completed successfully and created the verified,
encrypted snapshot:

```text
migration/supabase/exports/local-delivery/20260730T005031Z/
```

The encrypted archive is about 187 MiB. It contains the role settings,
application schema, database rows, Auth/Storage metadata, and exact
reconciliation counts captured from the same snapshot. The archive checksum
was verified before plaintext working files were removed. Its encryption key
remains in macOS Keychain under service
`GHOS Migration Export Encryption`, account `local-delivery`.

The export directory is intentionally ignored by Git. It still requires an
independent encrypted off-host copy before the migration backup gate is
complete.

## Security model

The 26 public RLS policies use four main patterns:

- service-role-only management for server-owned tables;
- authenticated reads for audit, Shopify update, and user-role data;
- active dispatch-user checks for notifications, subscriptions, and timing
  metrics;
- permission-aware authenticated CRUD for B2B companies.

`app_settings` and active origin information have intentionally broad read
policies. These policies must be reviewed in the local compatibility lab and
preserved or tightened deliberately; they must not disappear during a generic
schema conversion.

Dispatch V2 currently uses a service-role client in its server runtime. That
credential must remain server-only and be recreated for the self-hosted target.

No explicit `storage` schema policy was visible. The bucket itself is public,
and V2 performs Storage administration and uploads through its server-side
service-role client. This behavior must be reproduced intentionally rather than
assuming the public bucket permits anonymous writes.

The compatibility migration now explicitly recreates the standard Supabase
Data API object privileges for `anon`, `authenticated`, and `service_role`.
Managed Supabase normally supplies these outside application migrations, so
they were previously an undeclared self-hosting dependency. RLS remains
enabled on all 22 tables and continues to decide which rows each API role may
access.

## Realtime and scheduled database work

`dispatch_notifications` is the only public table currently included in the
`supabase_realtime` publication. No application cron jobs were found.

## Functions and triggers

Eight public trigger functions maintain `updated_at`:

- `set_dispatch_b2b_companies_updated_at`
- `set_dispatch_driver_locations_updated_at`
- `set_dispatch_employees_updated_at`
- `set_dispatch_orders_updated_at`
- `set_dispatch_routes_updated_at`
- `set_dispatch_stop_metrics_updated_at`
- `set_dispatch_trucks_updated_at`
- `set_dispatch_user_roles_updated_at`

Each function has a corresponding active `BEFORE UPDATE` row trigger. The
information-schema trigger view returned no rows to the MCP role, but a direct
read-only `pg_trigger` query confirmed all eight definitions.

## Edge Functions

Two managed Edge Functions are deployed:

- `shopify-api`
- `carrier-service`

The earlier Management API comparison found that deployed source matches the
local source; local `carrier-service` also contains a test file. Both functions
and their Shopify, Google, application, and Supabase secret values require
separate recreation and callback testing.

## Migration history

The managed project records four migration versions:

- `20260307154116`
- `20260309044312`
- `20260309052353`
- `20260310213433`

The V2 application also contains standalone SQL files for photo Storage,
driver/user links, B2B RLS, reliability indexes, timeout indexes, and learned
timing metrics. Those files are operational patches, not yet a complete
from-zero migration history. A canonical baseline must be generated from the
live schema and reconciled with these scripts before local restoration.

## Source binding

Canonical dispatch application:

```text
/Users/mattstengel/local-delivery/dispatch-v2-sandbox
```

Related shipping-calculator source:

```text
/Users/mattstengel/shipcalc2
```

Dispatch V2 directly depends on:

- Supabase Auth administration and login;
- Realtime;
- the 22-table public schema;
- public `dispatch-photos` Storage;
- Shopify import and update workflows;
- Google distance calculations;
- learned stop and round-trip timing data.

The local application source matches the newer live schema additions, including
B2B terms, employee/user linking, reliability indexes, and timing metrics.

## Comparison with GreenHills Quote Live

Both projects expose the same 22 public table names. Column-shape hashes match
exactly for 19 tables. Three Local-Delivery tables are newer:

| Table | Local-Delivery columns | Quote Live columns | Important additions |
|---|---:|---:|---|
| `custom_delivery_quotes` | 38 | 24 | company, terms, tax, billing, contact |
| `dispatch_b2b_companies` | 23 | 17 | tier, catalogs, terms, contact |
| `dispatch_employees` | 11 | 8 | Auth user link, user email, notes |

Row-count comparison:

| Table | Local-Delivery | Quote Live | Difference |
|---|---:|---:|---:|
| `app_audit_log` | 866 | 658 | +208 |
| `app_settings` | 16 | 16 | 0 |
| `app_user_profiles` | 12 | 11 | +1 |
| `custom_delivery_quotes` | 210 | 89 | +121 |
| `dispatch_audit_log` | 38,066 | 287 | +37,779 |
| `dispatch_b2b_companies` | 82 | 0 | +82 |
| `dispatch_driver_locations` | 11 | 8 | +3 |
| `dispatch_employees` | 12 | 11 | +1 |
| `dispatch_notifications` | 45 | 60 | -15 |
| `dispatch_orders` | 966 | 457 | +509 |
| `dispatch_push_subscriptions` | 2 | 2 | 0 |
| `dispatch_routes` | 24 | 22 | +2 |
| `dispatch_settings` | 2 | 2 | 0 |
| `dispatch_shopify_updates` | 156 | 40 | +116 |
| `dispatch_stop_metrics` | 588 | 90 | +498 |
| `dispatch_trucks` | 8 | 7 | +1 |
| `dispatch_user_roles` | 11 | 1 | +10 |
| `origin_addresses` | 5 | 5 | 0 |
| `product_source_map` | 144 | 128 | +16 |
| `Session` | 1 | 1 | 0 |
| `shipping_material_rules` | 9 | 9 | 0 |
| `shopify_app_settings` | 1 | 1 | 0 |

Local-Delivery is clearly the newer and more complete source, but Quote Live is
not proven to be a strict subset. Its extra notification rows and different
order-state distribution are enough to require an ID-based reconciliation
before the legacy dispatch copy is archived.

## Migration classification

This project requires:

1. PostgreSQL schema and data migration.
2. Supabase Auth migration for 13 users.
3. RLS, function, trigger, index, and Realtime verification.
4. Transfer and validation of 470 Storage objects.
5. Recreation of Edge Functions and secret values.
6. Shopify, Google, and public callback testing.
7. Dispatch V2 and ShipCalc application integration testing.
8. A rehearsed delta cutover after production writes are paused.
9. A rollback window that retains managed Supabase unchanged.

MCP is suitable for read-only discovery and controlled aggregate comparison. It
does not replace logical database dumps, Auth configuration exports, Storage
object transfer, secret inventory, or a cutover backup.

## Local recovery acceptance

The explicit Data API grants were applied to the local compatibility lab and
verified on 2026-07-29. Acceptance passed:

- the 22-table, 26-policy schema/RLS/reconciliation contract;
- anonymous settings access and protected-table RLS behavior;
- service-role access to the canonical dispatch schema;
- complete password Auth create, sign-in, password-change, logout, refresh
  revocation, and deletion lifecycle;
- invitation and password-recovery email flows through the local mail catcher;
- the `dispatch-photos` bucket contract; and
- `dispatch_notifications` Realtime publication membership.

The complete schema chain, including the explicit API grants, also rebuilt
successfully in a disposable PostgreSQL 17 database cloned from the local
Supabase platform. That database was removed afterward. Production rows,
identities, sessions, object bytes, and secrets were not involved.
