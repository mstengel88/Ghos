# Local-Delivery baseline source manifest

Status: compatibility rehearsal

The compatibility baseline combines MCP-confirmed live structure with the local
application SQL below. It is not yet the final production restore artifact.

## GHOS-owned compatibility files

- `000_foundation.sql`
- `850_live_indexes.sql`
- `900_live_contract.sql`
- `../../candidates/local-delivery/001_api_grants.sql`

The API-grants candidate makes the Data API role privileges supplied
implicitly by managed Supabase explicit for self-hosted recovery. All 22
tables have RLS enabled, so the grants permit PostgREST policy evaluation
without bypassing row security.

## Captured deployed Edge Functions

The following files are a byte-for-byte, secret-free capture of the functions
deployed to managed Local-Delivery on 2026-07-28. They also match the canonical
ShipCalc source. These hashes are enforced by
`tools/verify_local_delivery_edge_functions.sh`.

| Function source | SHA-256 |
|---|---|
| `functions/carrier-service/index.ts` | `5932eeaf6d969561b5279812d44d8c7c137a50080d6e1dfe2db7fd495c5f2354` |
| `functions/shopify-api/index.ts` | `e8fde587e01520d9c87a6dafec30baa5f3b3730d1b43a5d196fcb68c7d940aee` |
| `functions/shopify-api/shipping-calc.ts` | `2d9384f4d5219b32515aa274986b8db04dc8665eaf7626eedb262f21ed68e407` |

The compatibility lab mounts these sources with
`docker-compose.edge-functions.yml`. The override deliberately supplies empty
external-service credentials so local contract tests cannot contact Shopify or
Google.

### Review blocker before production cutover

`carrier-service/index.ts` currently reads `RATE_PER_MINUTE` inside
`getDriveTimeCost`, while that constant is declared only inside the request
handler. A real route calculation can therefore fail after Google returns a
valid route. The exact deployed source is preserved here as migration evidence;
the defect must be corrected and tested with mocked external APIs in a reviewed
candidate before the callback is moved away from managed Supabase.

## Canonical application SQL

Apply in this order:

1. `/Users/mattstengel/local-contractor/dispatch_schema.sql`
2. `/Users/mattstengel/local-contractor/supabase_auth_schema.sql`
3. `/Users/mattstengel/local-delivery/dispatch-v2-sandbox/sql/phase3_reliability.sql`
4. `/Users/mattstengel/local-delivery/dispatch-v2-sandbox/sql/driver_user_links.sql`
5. `/Users/mattstengel/local-contractor/supabase_security_hardening.sql`
6. `/Users/mattstengel/local-delivery/dispatch-v2-sandbox/sql/dispatch_b2b_companies_rls.sql`

`local-contractor` supplies historical from-zero table definitions only. It is
not the continuing dispatch application. Dispatch V2 remains authoritative for
runtime behavior and future schema changes.

## Deliberately excluded

- seed rows from ShipCalc migrations;
- all production rows;
- Auth identities and sessions;
- Storage objects;
- Shopify sessions and tokens;
- environment secrets;
- `dispatch_photo_storage.sql`, because the live project has no explicit
  `storage.objects` policy and `900_live_contract.sql` recreates the observed
  public-bucket contract;
- legacy performance patches not confirmed in the live index inventory.

Before production use, replace this multi-source rehearsal with a single
versioned migration generated from the verified local schema.

The clean-room rehearsal can be repeated only in the isolated, empty local lab:

```bash
ALLOW_LOCAL_REHEARSAL_RESET=yes tools/reset_local_delivery_rehearsal.sh
tools/rehearse_local_delivery_schema.sh
```

The rehearsal chain also applies the tracked self-hosting candidates in order:

1. `candidates/local-delivery/001_api_grants.sql`
2. `candidates/local-delivery/002_quote_tax_rate_cache.sql`

The second candidate brings the schema to the current 23-table production
contract while keeping the tax cache server-owned behind RLS.

The reset script refuses to run against any container name other than
`supabase-db` and aborts if application rows, Auth users, or dispatch photo
objects exist.

## Compatibility note

Five tables retain harmless physical column-order differences because their
definitions are assembled from historical migrations:

- `app_user_profiles`
- `dispatch_b2b_companies`
- `dispatch_orders`
- `dispatch_routes`
- `dispatch_trucks`

Column names, types, nullability, defaults, constraints, indexes, policies,
functions, and triggers are compared semantically. Application queries must
name columns explicitly and must not depend on `select *` column order.
