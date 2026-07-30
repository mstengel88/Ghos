# GHSSERVER Supabase migration workspace

This directory prepares the current managed Supabase workloads for an eventual
move to a self-hosted, Supabase-compatible data VM on GHSSERVER.

## Safety rules

- Managed Supabase remains the production source of truth until each app passes
  a documented cutover and rollback test.
- Never commit `.env` files, API keys, JWT secrets, database passwords, SMTP
  credentials, OAuth secrets, or production exports.
- Production discovery begins read-only.
- Every destructive or write-capable migration step requires a fresh database
  and storage backup.
- GHSSERVER deployment remains paused until diagnostics and replacement-memory
  testing are complete.

## Generate the local static inventory

Copy `apps.local.example.json` to the ignored `apps.local.json`, correct the
canonical app paths, and run:

```bash
python3 tools/supabase_inventory.py \
  --config migration/supabase/apps.local.json \
  --json-output migration/supabase/generated/inventory.json \
  --markdown-output migration/supabase/generated/INVENTORY.md
```

The scanner deliberately excludes environment-file values. Generated inventory
files are local evidence and are ignored until reviewed and deliberately added.

## Capture managed metadata

The authenticated Supabase CLI can capture project status, Edge Function
metadata, and secret names without retrieving secret values:

```bash
./tools/capture_supabase_management_inventory.sh
```

The output is written under the ignored `generated/managed/` directory. Review
`CURRENT-INVENTORY.md` for the approved snapshot.

Download the exact currently deployed Edge Function source into the ignored
export area:

```bash
./tools/download_deployed_supabase_functions.sh
```

This uses the Management API and does not deploy or modify functions.

## Export managed databases

Copy `managed-db.env.example` to the ignored
`secrets/managed-db.env`, enter the six connection URLs, and run:

```bash
./tools/dump_managed_supabase_projects.sh
```

The script follows Supabase's platform-to-self-hosted process by creating
separate role, schema, and data dumps. Exports are ignored, permission-restricted,
and checksummed. They still need encryption and an off-Mac backup.

After creating the guarded encrypted Local Delivery snapshot, rehearse it in a
disposable local database with:

```bash
./tools/rehearse_local_delivery_production_restore.sh
```

The rehearsal verifies outer and inner checksums, decrypts only into a private
temporary workspace, restores schema plus database/Auth/Storage metadata,
checks exact row counts and integrity, and removes the disposable database and
plaintext files after success or failure. It never targets managed Supabase or
the standing local lab.

## Export managed Storage

Storage object bytes can be exported independently while the exact database
connection remains gated. The exporter uses an existing server-side Supabase
service-role credential, never prints it or object paths, preserves the bucket
hierarchy, supports resuming complete files, and creates a SHA-256 manifest:

```bash
python3 tools/export_supabase_storage.py \
  --env-file /path/to/private/runtime.env \
  --bucket dispatch-photos \
  --output migration/supabase/exports/storage/local-delivery/initial
```

The output is ignored because object paths, metadata, and bytes may contain
sensitive production data. Encrypt it and retain an off-Mac copy before
considering the Storage backup gate complete.

Restore and byte-verify that private export only into the localhost compatibility
lab:

```bash
python3 tools/restore_supabase_storage.py \
  --env-file migration/supabase/runtime/stack/.env \
  --export migration/supabase/exports/storage/local-delivery/initial
```

The restore tool refuses non-localhost destinations, requires the bucket to
exist, verifies the private source manifest before uploading, resumes matching
objects, and downloads every restored object again for SHA-256 comparison.

## Prepare the self-hosted compatibility lab

```bash
./tools/prepare_supabase_lab.sh
```

This sparse-clones only the official `docker/` directory at the pinned commit,
copies it to the ignored runtime directory, and creates a private `.env`. It
does not start containers or access managed project data.

On macOS with Docker Desktop, start the PostgreSQL 17 lab with the tracked
Storage override. The override uses a Docker-managed Linux volume because the
macOS bind mount does not support the extended attributes required by the
Supabase Storage file backend:

```bash
cd migration/supabase/runtime/stack

docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  -f ../../docker-compose.mailpit.yml \
  up -d
```

The override is for the Mac compatibility lab only. The future Ubuntu VM uses
normal Linux storage and should not include it unless its backing filesystem
also lacks extended-attribute support.

The Mailpit override is also local-lab-only. It directs Supabase Auth email to
an internal SMTP catcher and exposes its review/API interface solely at
`http://127.0.0.1:8025`; no SMTP port is published to the host or LAN.

## Migration phases

1. Static source and migration inventory.
2. Read-only managed-project inventory.
3. Local self-hosted compatibility environment.
4. Repeatable database, Auth, Storage, and Edge Function export/import tools.
5. Application-by-application validation.
6. GHSSERVER data VM deployment after hardware validation.
7. Parallel production operation, controlled cutover, and rollback window.

See `MIGRATION-RUNBOOK.md` for acceptance gates.

## Local-Delivery data reconciliation

The verified schema baseline is followed by a local-only data reconciliation
stage. The ownership rules and acceptance gates are documented in
`data/local-delivery-reconciliation.md`.

Prepare the empty staging schema inside the isolated lab with:

```bash
docker exec -i supabase-db \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
  < migration/supabase/sql/prepare_reconciliation_staging.sql
```

After rebuilding the Local-Delivery schema, run the complete contract and
transaction-only RLS acceptance suite:

```bash
tools/verify_local_delivery_clean_room.sh
```

The suite verifies the live schema contract, prepares the isolated
reconciliation schema, exercises anonymous, authenticated, active, inactive,
administrator, and service-role visibility, and tests all four reconciliation
classifications plus reviewed merge decisions. It also verifies deterministic
quote creator remapping and the fail-closed quarantine for an unmapped creator.
Every synthetic fixture is rolled back, and reconciliation staging must remain
empty.

After exact encrypted exports are loaded and the private identity map is
approved, inspect quote readiness without exposing quote payloads:

```sql
select
  record_key,
  classification,
  decision,
  creator_resolution,
  ready_for_import
from migration_reconcile.quote_import_candidates
order by record_key;
```

Do not commit the contents of `migration_reconcile.identity_map` or any staged
row payload.

Run the disposable Auth session acceptance test separately:

```bash
tools/verify_local_delivery_auth_sessions.sh
```

It is guarded to the localhost lab and verifies password sign-in, profile
access, password replacement, old-password rejection, logout, refresh-token
revocation, administrator cleanup, and the absence of a retained test user.

With the local Mailpit override running, verify invitation and password-recovery
email end to end:

```bash
tools/verify_local_delivery_auth_email.sh
```

This test verifies capture, link redemption, session issuance, invited-user
profile access, recovered-password replacement, sign-in, and complete cleanup.

## Local-Delivery Edge Functions

Mount the captured deployed functions into the isolated lab without production
secrets:

```bash
cd migration/supabase/runtime/stack

docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  -f ../../docker-compose.mailpit.yml \
  -f ../../docker-compose.edge-functions.yml \
  up -d --no-deps functions
```

Then run the secret-free source and HTTP contract checks:

```bash
tools/verify_local_delivery_edge_functions.sh
```

These checks intentionally exercise only branches that cannot call Shopify or
Google. External callback acceptance remains blocked until test credentials or
mock endpoints are configured and the carrier-service rate-scope defect in the
captured production source is fixed in a reviewed migration candidate.

The reviewed candidate and its deterministic carrier test are documented in
`candidates/local-delivery/README.md`. With the candidate Compose override
mounted, run:

```bash
tools/verify_local_delivery_edge_candidate.sh
```

The test uses a temporary localhost-only Google route mock and no production
credentials.

The staging rows are sensitive temporary data. They must never be committed,
included in a routine schema dump, or copied into application fixtures.

## Dump Site schema rehearsal

The Dump Site source can be rehearsed without altering the Local-Delivery lab.
The verifier creates a disposable database inside the pinned PostgreSQL 17
container, applies all eight canonical migrations, exercises the queue
workflow, and removes the database afterward:

```bash
tools/verify_dump_site_schema.sh
```

Set `DUMP_SITE_SOURCE_ROOT` if the canonical Green Hills application repository
is stored somewhere other than its documented Mac path. Production data,
secrets, and external callbacks are deliberately excluded from this rehearsal.

## Ticket Printer compatibility rehearsal

Ticket Printer uses its own disposable PostgreSQL 17 database and temporarily
replaces only the local Edge Runtime function mounts. Both verifiers restore
or remove their temporary state on exit:

```bash
tools/verify_ticket_printer_schema.sh
tools/verify_ticket_printer_edge_functions.sh
```

The scripts default to `/Users/mattstengel/edit-my-ticket`. Set
`TICKET_PRINTER_SOURCE_ROOT` when the source checkout lives elsewhere.

The schema verifier intentionally excludes the managed Supabase `pg_cron`
migration. GHOS replaces that schedule with the root-only systemd unit under
`ops/ticket-printer`. The Edge verifier forces all third-party credentials
empty and uses the tracked deployed `loadrite-sync` baseline.
