# Local-Delivery baseline source manifest

Status: compatibility rehearsal

The compatibility baseline combines MCP-confirmed live structure with the local
application SQL below. It is not yet the final production restore artifact.

## GHOS-owned compatibility files

- `000_foundation.sql`
- `850_live_indexes.sql`
- `900_live_contract.sql`

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
