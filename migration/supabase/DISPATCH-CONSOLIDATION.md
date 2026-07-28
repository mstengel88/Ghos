# Dispatch consolidation decision

Decision date: 2026-07-27

Status: accepted

## Decision

Dispatch V2 Sandbox is the only dispatch application that will remain in
operation:

```text
/Users/mattstengel/local-delivery/dispatch-v2-sandbox
```

The older dispatch implementation associated with
`/Users/mattstengel/local-contractor` and the GreenHills Quote Live Supabase
project will be retired. The quote tool itself remains in scope and must not be
retired with the old dispatch UI.

## Ownership after consolidation

| Capability | Continuing owner |
|---|---|
| Dispatch UI and workflow | Dispatch V2 Sandbox |
| Dispatch schema and future migrations | Local-Delivery / Dispatch V2 |
| Dispatch integration inside GHOS | Dispatch V2 |
| Quote workflow | GreenHills Quote Live / `local-contractor` until migrated |
| Legacy dispatch history | Preserved, reconciled, then archived |

No new feature should be added to the old dispatch implementation. Bug fixes
there should be limited to what is necessary to preserve production operation
until cutover.

## Safe consolidation sequence

1. Inventory both dispatch schemas, functions, policies, buckets, and
   application dependencies.
2. Identify records and fields that exist only in the legacy Quote Live copy.
3. Establish a versioned PostgreSQL 17 migration baseline owned by Dispatch V2.
4. Define deterministic identity matching for orders, routes, stops, employees,
   trucks, photos, notifications, metrics, and audit history.
5. Restore both schemas into the isolated local lab and rehearse the merge using
   non-production data.
6. Validate counts, foreign-key relationships, active schedules, route state,
   delivery windows, round-trip times, proof-of-delivery data, and audit
   history.
7. Keep the old dispatch available while V2 and GHOS are acceptance-tested.
8. Schedule a write freeze, take the final delta export, run the rehearsed
   merge, and switch integrations to V2.
9. Observe the new runtime before disabling the old dispatch application.
10. Revoke legacy credentials and remove old runtime components only after
    explicit approval. Retain a recoverable database backup according to the
    agreed retention policy.

## Non-goals

- Do not merge the two dispatch applications at the UI level.
- Do not keep two writable dispatch systems after cutover.
- Do not delete legacy production data merely because the old app is being
  retired.
- Do not move the quote tool into the retirement scope.

## Immediate migration consequence

All future dispatch-related compatibility work must evaluate the V2 Sandbox
implementation first. GreenHills Quote Live dispatch objects are evidence and
migration inputs, not the authority for new schema design.
