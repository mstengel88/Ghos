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

## Next acceptance step

The lab is ready for isolated schema compatibility testing. No application
should point at it yet. The next step is to compare Local-Delivery with the
legacy dispatch schema in Quote Live, establish Dispatch V2 Sandbox as the
canonical migration baseline, and restore a non-production schema copy before
transferring any production rows. The quote tool remains a continuing
application even though its older dispatch implementation will be retired.

The read-only comparison is complete and documented in
`projects/local-delivery.md`. The next lab action is to generate a from-zero
Local-Delivery baseline, restore it without production rows, and verify all 22
tables, 26 policies, eight functions/triggers, 106 indexes, Auth dependencies,
Realtime behavior, and the `dispatch-photos` bucket contract.
