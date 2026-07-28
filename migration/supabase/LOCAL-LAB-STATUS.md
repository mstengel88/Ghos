# Local Supabase compatibility lab status

Last verified: 2026-07-27

## Baseline

- Host: macOS with Docker Desktop 29.6.2
- Docker Compose: 5.3.1
- Supabase source: official pinned Docker stack recorded in
  `runtime/UPSTREAM_COMMIT`
- PostgreSQL image: `supabase/postgres:17.6.1.136`
- PostgreSQL server: 17.6
- Exposure: localhost only
- Production credentials or exports loaded: no

## Running services

All 11 application services are running and healthy:

- Auth
- PostgreSQL
- Edge Functions
- imgproxy
- Kong
- postgres-meta
- Realtime
- PostgREST
- Storage
- Studio
- Supavisor

## Verification

The official self-hosted smoke test passed 35 of 35 checks:

- container health;
- Studio authentication and anonymous rejection;
- Auth create, sign-in, profile, and delete lifecycle;
- PostgREST with anonymous and service-role keys;
- GraphQL endpoint health with the extension disabled by default;
- Storage bucket, upload, download, hash, signed URL, and cleanup;
- TUS resumable upload and integrity checks;
- Edge Function invocation;
- postgres-meta authorization;
- MCP routes blocked by default;
- Realtime health and protected administration routes.

Public signup was intentionally skipped because email autoconfirm is disabled.

Post-test cleanup was verified:

- Auth users: 0
- Storage buckets: 0
- Storage objects: 0

## macOS Storage compatibility

Supabase Storage's file backend requires Linux extended attributes. A normal
Docker Desktop bind mount from macOS returned `ENOTSUP` during uploads.

`docker-compose.macos-storage.yml` replaces the Storage and imgproxy bind mounts
with the named Docker volume `ghos-supabase-storage-data`. This resolved both
normal and resumable uploads. The official upstream compose files remain
unchanged.

Start or reconcile the lab with:

```bash
cd migration/supabase/runtime/stack

docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  up -d
```

Stop the containers without deleting the database or Storage volumes:

```bash
docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  stop
```

Do not use `down --volumes` after migration data has been loaded.

## Local-Delivery schema rehearsal

The read-only managed-project comparison is documented in
`projects/local-delivery.md`. Dispatch V2 Sandbox is the canonical continuing
dispatch runtime; the older dispatch implementation will be retired after data
reconciliation, while its quote tool remains in migration scope.

A schema-only Local-Delivery compatibility baseline has been restored into the
lab without production rows, Auth identities, secrets, or Storage objects. The
rehearsal matches the managed project contract:

- 22 public tables, all with RLS enabled;
- 26 public policies;
- eight public functions and eight active application triggers;
- 106 public indexes;
- exact column names, types, nullability, and defaults for all 22 tables;
- matching primary, foreign-key, and check constraints;
- `dispatch_notifications` in the Supabase Realtime publication;
- public `dispatch-photos` bucket metadata, without production objects.

Five tables have physical column-order drift inherited from historical source
migrations. Semantic signatures match, and the difference is documented in the
baseline source manifest. The final consolidated migration should preserve the
managed order, but application compatibility does not depend on it.

The pre-rehearsal local schema-only backup is:

```text
/tmp/ghos-local-lab-before-local-delivery-20260727.sql
SHA-256: 6e8d4d089481a9a5913d14ed39fda605cdf9f9763c3ff1d3884a29f04150da12
```

No application should point at the lab yet. The next acceptance step is a
repeatable clean-room restore followed by a sanitized data rehearsal and
cross-project reconciliation plan.

## Reconciliation staging

The local-only `migration_reconcile` staging schema is installed and its
classification logic has passed a transaction-only synthetic test. It:

- compares Local-Delivery and Quote Live rows by table and deterministic key;
- ignores fields absent from the older project shape while comparing all
  fields shared by both projects;
- classifies canonical-only, legacy-only, matching, and conflicting rows;
- records explicit merge decisions;
- refuses readiness when either checked import manifest is missing;
- refuses readiness when loaded table/row totals differ from a manifest;
- refuses readiness while any legacy-only or conflicting row lacks a decision.

The staging schema is empty. No production rows or credentials have been loaded.
Bulk rehearsal remains blocked on encrypted database exports or direct
read-only database connections; MCP remains the read-only inventory channel.
