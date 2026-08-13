# GHOS Operations Supabase

This is the isolated, lightweight self-hosted Supabase runtime for Ticket
Printer and Dump Site. It intentionally does **not** contain Local-Delivery or
WinterWatch-Pro.

The stack includes PostgreSQL 17, Auth, PostgREST, Edge Runtime, and the Envoy
API gateway. Studio, Realtime, Storage, Analytics, Vector, and Supavisor are
omitted because neither application currently uses them. The database is not
published to the host. Envoy binds only to loopback until an approved
Cloudflare hostname is configured.

## Safety boundaries

- Ticket Printer Edge Functions require a valid user JWT unless their own code
  adds a stricter check.
- Only `dump-site-api` and `dump-site-bridge` bypass the runtime JWT check; both
  retain their existing QR/bridge-secret authorization.
- Runtime secrets and generated files are ignored by Git.
- Managed Supabase remains the rollback source until exact export, restore,
  acceptance, backup, and observation gates pass.

## Prepare on GHOS

```bash
cd /opt/ghos/ops/supabase-operations
./generate-env.sh

# Copy the Dump Site source checkout to this default path, or set
# DUMP_SITE_SOURCE to its functions directory.
sudo install -d -o ghosadmin -g ghosadmin /opt/ghos/apps/dump-site-source

./prepare-runtime.sh
docker compose --env-file .env -f compose.yml config --quiet
```

Complete the root-private `.env` and `functions.env` files before starting.
Never paste their values into Git, screenshots, or chat.

On GHOS, existing approved Google Maps, Shopify app, and Loadrite user values
can be copied into the private integration file without displaying them:

```bash
./import-known-integrations.sh
./generate-dump-site-secrets.sh
./validate-integrations.sh
```

The validator intentionally blocks cutover until the required Ticket Printer
and Dump Site values are present. Managed Supabase does not expose Edge Function
secret values through database temporary access, so any remaining values must
come from the approved password manager or be rotated and installed in both the
dependent client and this private file.

`generate-dump-site-secrets.sh` only fills blank values and never rotates an
existing value. The new QR token must be installed in the Dump Site client and
the bridge secret must be installed in the CounterPoint bridge before traffic
is switched.

```bash
docker compose --env-file .env -f compose.yml up -d
./verify.sh
```

## Data cutover gates

1. Capture and verify exact encrypted exports for Ticket Printer and Dump Site.
2. Restore Ticket Printer database/Auth as the platform baseline.
3. Apply the eight Dump Site migrations and import only its three application
   tables plus both sequence positions.
4. Compare exact row counts and run RLS/Auth/function acceptance.
5. Register the database, runtime configuration, and source directories with
   GHOS backups; run and restore-test a backup.
6. Build Ticket Printer against the candidate public URL and keys.
7. Point Dump Site clients at the candidate HTTPS function URL.
8. Observe the candidate, then switch traffic. Keep managed Supabase intact
   through the rollback window.

Do not run two whole-platform restores into this shared runtime. Ticket Printer
owns the platform/Auth baseline; Dump Site is imported as application-only
schema and data.

## Backup registration

The normal GHOS source backup already includes this stack because `/opt/ghos`
is a required source path. After the Operations database is restored and the
stack is healthy, run `ghos-backup-register-current-workloads`. It discovers
the single non-template Operations database used by the healthy runtime, which
also supports a timestamped database name retained during a rollback window.
The resulting logical-dump entry in `/etc/ghos-backup/databases.conf` has this
shape:

```text
operations|/opt/ghos/ops/supabase-operations/compose.yml|db|postgres|<active-database-name>|supabase/postgres:17.6.1.136
```

Do not enable the scheduled backup timer until an Operations backup and a
disposable restore drill both pass. Docker volume files are not a substitute
for the consistent PostgreSQL logical dump.

## Managed-source access required

The exact export scripts use Supabase temporary database access and never reset
the database password. Before running them, enable temporary access for the
current Supabase user and map that user to the `postgres` role in both managed
projects:

- Ticket Printer: `dlayrpnmfnbjlxgnkczv`
- Dump Site: `bnethnlrhwcjgjgjvoxz`

Then run:

```bash
./tools/export_ticket_printer_database.sh
./tools/export_dump_site_database.sh
./tools/rehearse_operations_restore.sh
```

An export is not accepted unless its encrypted archive, checksum manifest, and
snapshot-specific expected row counts all validate. The rehearsal restores both
sources into a disposable database, compares every protected row count and both
Dump Site sequence positions, verifies the combined schema contract, confirms
that Dump Site has no browser-role table grants, and removes the rehearsal
database and plaintext working files when it exits.
