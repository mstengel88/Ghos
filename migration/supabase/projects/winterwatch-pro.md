# WinterWatch-Pro migration contract

Managed project: WinterWatch-Pro (`caegybyfdkmgjrygnavg`)

Canonical source: `/Users/mattstengel/winterwatch`

Status: read-only live inventory and exact PostgreSQL 17 application-schema
rehearsal passed. Production rows, Auth identities, Storage objects, secrets,
Realtime behavior, and external callbacks have not been migrated.

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
- one private Storage bucket, `work-photos`;
- 92 Storage objects totaling 232,094,733 bytes;
- one Realtime publication table: `public.employee_locations`;
- three active managed cron jobs, scheduled every 15 minutes, every 5 minutes,
  and daily at 3:00 AM; and
- seven active Edge Functions.

Public-table row counts:

| Table | Rows |
|---|---:|
| `accounts` | 24 |
| `audit_logs` | 838 |
| `employee_locations` | 1 |
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

All seven normalized schema fingerprints match the managed project exactly:

| Surface | Fingerprint |
|---|---|
| Columns | `af255063b9bcca0dbb09068bbdd40cce` |
| Constraints | `ffdc7efa258adff6e793410c893abe29` |
| Indexes | `b0468a2f6b2809b48f5f8a974ae18413` |
| Functions | `7c498acbfa9b299df7f0d57c98521e3a` |
| Triggers | `6d94e340996d83d8fdfe360dba202aae` |
| Policies | `82388cf87708f568986c16daece3d9c3` |
| Enums | `817be365087d37cb6289998376694943` |

The prior report of 47 public functions included 36 `pgcrypto` extension
functions because the rehearsal installed that extension into `public`.
The verifier now installs `pgcrypto` into the Supabase-compatible `extensions`
schema and counts the correct 11 application functions.

Run it with:

```bash
tools/verify_winterwatch_schema.sh
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

1. Capture an encrypted production database/Auth export without resetting the
   managed database password.
2. Export and hash every private `work-photos` object and its metadata.
3. Restore database, Auth, and Storage into the isolated lab and validate RLS.
4. Test approved staging credentials for every external Edge Function service.
5. Run the WinterWatch web/mobile client against the candidate backend through
   environment configuration. The environment-switchable client is committed
   on WinterWatch branch `codex/self-hosted-supabase-config` at `337bf8f`.
6. Complete a backup/restore drill and a rehearsed maintenance-window cutover.
7. Keep managed Supabase intact through the rollback observation window.
