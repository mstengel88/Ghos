# WinterWatch-Pro migration contract

Managed project: WinterWatch-Pro (`caegybyfdkmgjrygnavg`)

Canonical source: `/Users/mattstengel/winterwatch`

Status: live inventory, exact PostgreSQL 17 application-schema rehearsal, and
an isolated production database/Auth/Storage-metadata restore rehearsal passed.
All private Storage objects have been exported, copied to the GHOS VM, restored
into the isolated lab, and byte-verified. Secrets, Realtime client behavior,
and external callbacks have not been migrated.

## Read-only managed inventory

Captured 2026-07-28 without retrieving row payloads, identities, objects, or
secret values:

- PostgreSQL 17.6 in `us-east-1`, reporting healthy;
- database size: 87,256,211 bytes;
- 20 public tables, all with RLS enabled;
- 74 public RLS policies;
- 11 application functions;
- 19 application triggers;
- six public enum types, no public views, and no public sequences;
- 12 Auth users, 13 identities, and 20 sessions;
- one private Storage bucket, `work-photos`, with no size or MIME restriction;
- six Storage object policies that reproduce exactly in the local rehearsal;
- 92 Storage objects totaling 232,094,733 bytes: 89 JPEG and three PNG files,
  created from 2026-01-17 through 2026-03-17;
- one Realtime publication table: `public.employee_locations`;
- three active managed cron jobs, scheduled every 15 minutes, every 5 minutes,
  and daily at 3:00 AM; and
- seven active Edge Functions.

Public-table row counts:

| Table | Rows |
|---|---:|
| `accounts` | 24 |
| `audit_logs` | 840 |
| `employee_locations` | 3 |
| `employees` | 15 |
| `equipment` | 14 |
| `maintenance_logs` | 1 |
| `maintenance_notification_settings` | 5 |
| `maintenance_requests` | 4 |
| `notification_preferences` | 1 |
| `notification_types` | 4 |
| `notifications_log` | 183 |
| `overtime_notification_settings` | 2 |
| `overtime_notifications_sent` | 26 |
| `profiles` | 12 |
| `push_device_tokens` | 8 |
| `scheduled_notifications` | 0 |
| `shovel_work_logs` | 57 |
| `time_clock` | 38 |
| `user_roles` | 15 |
| `work_logs` | 166 |

These are reconciliation counts, not a data export. They will be captured
again immediately before and after the isolated restore.

## Managed collation repair

On 2026-07-28, the live application database reported a stored collation
version of `153.120` while the operating-system collation provider reported
`153.121`. The same production-safe repair already proven on Local Delivery
and Ticket Printer was applied:

- all eight valid, ready, application-owned indexes with collation
  dependencies were reindexed;
- `ALTER DATABASE postgres REFRESH COLLATION VERSION` completed successfully;
- the live `postgres` database now reports `153.121` for both the stored and
  actual collation versions; and
- post-repair verification found zero invalid or unready indexes.

The Supabase-owned `template1` database still reports stored version `153.120`
and actual version `153.121`. The temporary-access `postgres` role does not own
`template1` and is not a member of its `supabase_admin` owner, so refreshing it
requires Supabase platform ownership. This does not block WinterWatch traffic,
the production export, or the isolated restore.

Post-repair reconciliation preserved the schema fingerprints and current
application totals. The only count changes from the earlier read-only snapshot
were normal live activity: `audit_logs` increased from 838 to 840 and
`employee_locations` increased from one to three. Auth remains at 12 users and
13 identities, Storage remains at 92 verified objects, and all seven Edge
Functions remain active. Both local verification commands pass:

```bash
tools/verify_winterwatch_schema.sh
tools/verify_winterwatch_edge_functions.sh
```

## Encrypted database and Auth export

An encrypted production export was captured on 2026-07-29 through Supabase
temporary database access without resetting the managed database password. It
contains:

- the filtered role settings produced by Supabase's official migration script;
- the application schema;
- all application rows;
- 12 Auth users and 13 Auth identities; and
- one Storage bucket plus metadata for 92 private objects.

The official Supabase dump scripts are pinned to CLI commit
`ac24960aeccfd7b2cfc0e59629c732f03f1a55a8` and verified by SHA-256 before
execution. Unencrypted SQL exists only in a private temporary directory. The
final AES-256-CBC/PBKDF2 archive is ignored by Git at:

```text
migration/supabase/exports/winterwatch-pro/20260729T115619Z/
```

Its encryption password is stored in macOS Keychain under service
`GHOS Migration Export Encryption`, account `winterwatch-pro`; it is not in
source control or this document. A sealed recovery copy of that password is
still required.

Future exports use:

```bash
tools/export_winterwatch_database.sh
```

## Isolated production restore rehearsal

The encrypted export was decrypted locally and restored into the isolated
database `winterwatch_rehearsal_20260729` in the pinned self-hosted Supabase
PostgreSQL 17 lab. The existing Local-Delivery `postgres` database and its data
were not reset or overwritten.

The rehearsal verified:

- all 20 public tables and their expected production row counts;
- RLS enabled on all 20 public tables and all 74 public policies;
- 12 Auth users and 13 identities with zero orphan identities;
- 12 profiles with zero orphan profiles;
- one Storage bucket and metadata for all 92 objects;
- zero invalid or unready indexes; and
- zero unvalidated application/Auth/Storage constraints.

Two managed-schema compatibility differences were handled only in temporary
rehearsal copies; the signed production export was never edited:

- `pg_cron` can only be installed in the self-hosted stack's configured
  `postgres` database, so extension creation is skipped in the isolated clone.
  GHOS already has the reviewed systemd scheduler replacement.
- managed Auth has
  `auth.custom_oauth_providers.custom_claims_allowlist`, while the pinned
  self-hosted Auth schema does not. The production table contains zero rows.
  The rehearsal removes only that empty COPY header column and refuses to
  continue if the table ever contains rows.

Repeatable tooling now lives at:

```bash
tools/rehearse_winterwatch_restore.sh
tools/verify_winterwatch_restore.sh
```

Cluster-wide role settings are skipped by default in the shared compatibility
lab. They can be applied only by explicitly setting
`WINTERWATCH_APPLY_CLUSTER_ROLES=1` on a disposable target cluster.

## PostgreSQL 17 rehearsal

`tools/verify_winterwatch_schema.sh` applies the portable application
migrations to a disposable PostgreSQL 17 database. It creates compatibility
stubs for the Supabase Auth and Storage schemas but copies no production user,
session, or object data.

The canonical local source contains 36 migration files, but the managed
migration history is not the same 36-file set. Managed Supabase contains a
`20260205131156` migration that is absent locally; the local source instead
contains a later `20260517101000_add_dispatch_driver_role.sql`. The missing
managed migration created `maintenance_logs`. Rather than rewriting history,
`migration/supabase/baselines/winterwatch/900_live_contract.sql` records that
live-only table, its two foreign keys, four policies, and updated-at trigger.

After applying 34 portable source migrations plus that explicit reconciliation,
the verified live contract is:

- 34 portable application migrations;
- one live reconciliation migration;
- 20 public tables, all with RLS enabled;
- 74 public RLS policies;
- 11 public application functions; and
- 19 application triggers.

All eight normalized schema and Storage-policy fingerprints match the managed
project exactly:

| Surface | Fingerprint |
|---|---|
| Columns | `af255063b9bcca0dbb09068bbdd40cce` |
| Constraints | `ffdc7efa258adff6e793410c893abe29` |
| Indexes | `b0468a2f6b2809b48f5f8a974ae18413` |
| Functions | `7c498acbfa9b299df7f0d57c98521e3a` |
| Triggers | `6d94e340996d83d8fdfe360dba202aae` |
| Policies | `82388cf87708f568986c16daece3d9c3` |
| Enums | `817be365087d37cb6289998376694943` |
| Storage policies | `a0937b9aabadcb006da6f84f04d1c8d9` |

The prior report of 47 public functions included 36 `pgcrypto` extension
functions because the rehearsal installed that extension into `public`.
The verifier now installs `pgcrypto` into the Supabase-compatible `extensions`
schema and counts the correct 11 application functions.

Run it with:

```bash
tools/verify_winterwatch_schema.sh
```

## Private Storage export

The 92 `work-photos` objects require a server-side service-role credential;
the Supabase MCP connection intentionally does not expose that secret. Put the
existing WinterWatch service-role key in the ignored file
`migration/supabase/secrets/winterwatch-storage.env`:

```dotenv
SUPABASE_URL=https://caegybyfdkmgjrygnavg.supabase.co
SUPABASE_SERVICE_ROLE_KEY=replace-with-existing-service-role-key
```

Protect and run it:

```bash
chmod 600 migration/supabase/secrets/winterwatch-storage.env
tools/export_winterwatch_storage.sh
```

The wrapper refuses the wrong project URL, a missing key, or loose file
permissions. The generic exporter does not print credentials or object names,
resumes completed files, and writes an ignored SHA-256 manifest beside the
downloaded bytes.

The initial private export completed on 2026-07-28:

- 92 of 92 objects;
- 232,094,733 bytes;
- 92 locally verified files; and
- valid manifest checksum
  `382e8555cec5771f9a286e77c469795da84ac5548d9165e96103b7bd275db580`.

The tracked, non-sensitive checkpoint is
`migration/supabase/data/winterwatch-storage-manifest-20260728.md`. The ignored
private export was copied to the GHOS VM and independently verified there with
zero mismatches. It was also restored into the isolated localhost Supabase lab:
92 objects and 232,094,733 bytes were uploaded and downloaded again with zero
SHA-256 mismatches.

The VM copy is registered with the root-only GHOS backup source list. Encrypted
Backblaze B2 snapshot `39e3ff2d` completed successfully on 2026-07-28 with
process exit status zero. The registration remains idempotent and guarded by:

```bash
cd /opt/ghos
sudo ./tools/register_winterwatch_storage_backup.sh
```

## Managed scheduler replacement

Two of the 36 historical migrations are infrastructure-specific:

- one installs `pg_cron` and `pg_net`; and
- one schedules the `check-overtime` Edge Function every five minutes.

They are intentionally excluded from the portable schema rehearsal. The
self-hosted replacement is:

- `ops/winterwatch/ghos-winterwatch-check-overtime`;
- `ghos-winterwatch-check-overtime.service`; and
- `ghos-winterwatch-check-overtime.timer`.

The timer only accepts a loopback Edge Function URL. Its API key belongs in
`/etc/ghos-winterwatch/check-overtime.env`, outside Git with root-only
permissions. Do not enable it until the WinterWatch self-hosted Supabase stack,
OneSignal configuration, and backup/restore drill are ready.

The historical scheduler migration contains an environment-specific anonymous
token. It must not be reused for the self-hosted environment, copied into a
container image, or treated as a secret handoff. The future baseline should
replace that migration with the systemd unit and a newly generated local key.

## Platform dependencies

WinterWatch uses more than PostgreSQL:

- Supabase Auth and Auth-triggered profile creation;
- private `work-photos` Storage with access policies tied to work logs;
- Realtime/client subscriptions that still need an exact inventory;
- seven Edge Functions;
- OneSignal push notifications;
- weather, Home Assistant, and Google Drive integrations; and
- email/maintenance notification workflows.

`tools/verify_winterwatch_edge_functions.sh` mounts all seven functions in the
local Edge Runtime with external credentials empty. It verifies their
secret-free and missing-authentication behavior without calling OneSignal,
Google Drive, Home Assistant, weather, or any production Supabase endpoint.

The deployed `home-assistant` function differs from local source only in
TypeScript result typing/casts; its queries and response behavior match. The
typed local version is the self-hosting candidate, while the deployed bundle
remains retained as migration evidence.

## Remaining gates

1. Store a sealed recovery copy of the database-export encryption password.
2. Test Auth and Storage through the full self-hosted APIs against the restored
   candidate, including a sample of the separately restored object binaries.
3. Test approved staging credentials for every external Edge Function service.
4. Run the WinterWatch web/mobile client against the candidate backend through
   environment configuration. The environment-switchable client is committed
   on WinterWatch branch `codex/self-hosted-supabase-config` at `337bf8f`.
5. Complete a full database/Auth backup/restore drill and a rehearsed
   maintenance-window cutover.
6. Keep managed Supabase intact through the rollback observation window.
