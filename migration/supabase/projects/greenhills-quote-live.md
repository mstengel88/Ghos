# GreenHills Quote Live — managed project inventory

Inventory date: 2026-07-27

Project ref: `dbyxbgbkokcddgeybjmf`

Inventory method: official Supabase MCP, project-scoped to GreenHills Quote
Live with read-only database access. No production rows containing customer
details, credentials, tokens, or secret values were exported.

## Executive summary

GreenHills Quote Live is an active production database, not an empty or
quote-only project. It contains the quote tool, Shopify shipping configuration,
and a populated copy of the dispatch data model.

The compact Supabase table listing reported stale planner estimates for several
tables. Exact read-only `COUNT(*)` queries were used for the counts below.

- PostgreSQL: 17.6
- Database size: 205 MB
- Public tables: 22
- Public RLS policies: 20
- Public functions: 8
- Public indexes visible to the read-only connection: 96
- Auth users: 11
- Storage buckets: 1
- Storage objects: 0
- Supabase migration history entries: 4

## Exact public table counts

| Table | Rows |
|---|---:|
| `app_audit_log` | 658 |
| `app_settings` | 16 |
| `app_user_profiles` | 11 |
| `custom_delivery_quotes` | 89 |
| `dispatch_audit_log` | 287 |
| `dispatch_b2b_companies` | 0 |
| `dispatch_driver_locations` | 8 |
| `dispatch_employees` | 11 |
| `dispatch_notifications` | 60 |
| `dispatch_orders` | 457 |
| `dispatch_push_subscriptions` | 2 |
| `dispatch_routes` | 22 |
| `dispatch_settings` | 2 |
| `dispatch_shopify_updates` | 40 |
| `dispatch_stop_metrics` | 90 |
| `dispatch_trucks` | 7 |
| `dispatch_user_roles` | 1 |
| `origin_addresses` | 5 |
| `product_source_map` | 128 |
| `Session` | 1 |
| `shipping_material_rules` | 9 |
| `shopify_app_settings` | 1 |

## Installed extensions

- `pg_stat_statements` 1.11
- `pg_trgm` 1.6
- `pgcrypto` 1.3
- `plpgsql` 1.0
- `supabase_vault` 0.3.1
- `uuid-ossp` 1.1

The local PostgreSQL 17 target must enable compatible versions of the
application-required extensions before restoring schema or data. Supabase
platform-only extensions and schemas need explicit compatibility testing.

## Auth and Storage

- `auth.users` contains 11 users.
- The public profile model also contains 11 rows.
- The `dispatch-photos` bucket exists and is public.
- The bucket currently contains zero objects.
- Its configured size limit is 10 MiB.
- Allowed types are JPEG, PNG, WebP, HEIC, and HEIF.

Auth identities must be migrated as an Auth subsystem, not reconstructed from
the public profile table. Existing sessions and JWTs should be treated as
environment-specific.

## Security model

RLS is enabled on all 22 public tables.

The 20 visible public policies primarily use one of these patterns:

- service-role-only access for application and dispatch tables;
- public or anonymous read access for active origin/settings data;
- authenticated self-read access for user profiles;
- authenticated role/permission checks for B2B companies.

Storage has a public-read policy for objects in `dispatch-photos`.

The self-hosted target must preserve RLS and policy behavior before any client
is switched to it. Service-role usage in applications must be inventoried
separately and replaced with target-environment credentials.

## Functions and database objects

Eight public trigger functions exist for maintaining `updated_at` values on
dispatch tables:

- `set_dispatch_b2b_companies_updated_at`
- `set_dispatch_driver_locations_updated_at`
- `set_dispatch_employees_updated_at`
- `set_dispatch_orders_updated_at`
- `set_dispatch_routes_updated_at`
- `set_dispatch_stop_metrics_updated_at`
- `set_dispatch_trucks_updated_at`
- `set_dispatch_user_roles_updated_at`

No public views, materialized views, or sequences were returned. The read-only
metadata query did not expose any installed triggers even though the trigger
functions exist. This must be verified against the canonical local schema
before restore; the functions may currently be orphaned or trigger metadata
may be hidden from the MCP read-only role.

## Source binding and drift

The live schema strongly matches `/Users/mattstengel/local-contractor`,
including:

- `custom_delivery_quotes`
- `product_source_map`
- Shopify session and settings tables
- shipping material rules and origin addresses
- dispatch tables and supporting audit/notification tables

This confirms `local-contractor` as the primary source candidate for this
managed project. It also shares schema concepts with Dispatch V2, so ownership
of common dispatch migrations must be consolidated before self-hosting.

Local SQL contains newer B2B quote fields and reliability/index work that are
not all visible in the live table shape. Those files must not be applied
blindly. A schema-diff migration should be generated and tested against a local
PostgreSQL 17 copy first.

## Migration classification

This project requires all of the following:

1. PostgreSQL schema and data migration.
2. Supabase Auth migration for 11 users.
3. RLS policy and role verification.
4. Shopify session-token handling without exposing token values in source.
5. Storage bucket/config recreation; no object transfer is currently required.
6. Environment secret recreation.
7. Quote and dispatch application integration testing.
8. A planned cutover with a final delta export after writes are paused.

MCP is suitable for read-only discovery and structured row export. It does not
replace a full logical database backup, Auth configuration export, secret
inventory, or cutover procedure.
