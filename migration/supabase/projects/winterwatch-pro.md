# WinterWatch-Pro migration contract

Managed project: WinterWatch-Pro (`caegybyfdkmgjrygnavg`)

Canonical source: `/Users/mattstengel/winterwatch`

Status: local PostgreSQL 17 application-schema rehearsal passed. Production
rows, Auth identities, Storage objects, secrets, Realtime behavior, and
external callbacks have not been migrated.

## PostgreSQL 17 rehearsal

`tools/verify_winterwatch_schema.sh` applies the portable application
migrations to a disposable PostgreSQL 17 database. It creates compatibility
stubs for the Supabase Auth and Storage schemas but copies no production user,
session, or object data.

The verified contract is:

- 34 portable application migrations;
- 19 public tables, all with RLS enabled;
- 70 public RLS policies;
- 47 public functions; and
- 18 application triggers.

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

1. Capture exact managed schema metadata, row counts, Auth totals, Storage
   object counts/bytes, Realtime publications, and policy definitions.
2. Capture an encrypted production database/Auth export without resetting the
   managed database password.
3. Export and hash every private `work-photos` object and its metadata.
4. Restore database, Auth, and Storage into the isolated lab and validate RLS.
5. Test approved staging credentials for every external Edge Function service.
6. Run the WinterWatch web/mobile client against the candidate backend through
   environment configuration.
7. Complete a backup/restore drill and a rehearsed maintenance-window cutover.
8. Keep managed Supabase intact through the rollback observation window.
