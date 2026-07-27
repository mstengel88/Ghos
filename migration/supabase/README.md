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

## Prepare the self-hosted compatibility lab

```bash
./tools/prepare_supabase_lab.sh
```

This sparse-clones only the official `docker/` directory at the pinned commit,
copies it to the ignored runtime directory, and creates a private `.env`. It
does not start containers or access managed project data.

## Migration phases

1. Static source and migration inventory.
2. Read-only managed-project inventory.
3. Local self-hosted compatibility environment.
4. Repeatable database, Auth, Storage, and Edge Function export/import tools.
5. Application-by-application validation.
6. GHSSERVER data VM deployment after hardware validation.
7. Parallel production operation, controlled cutover, and rollback window.

See `MIGRATION-RUNBOOK.md` for acceptance gates.
