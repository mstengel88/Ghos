# Ticket Printer migration

Last verified: 2026-07-28

Ticket Printer currently uses managed project `dlayrpnmfnbjlxgnkczv`. Its
primary local source is `/Users/mattstengel/edit-my-ticket`.

## PostgreSQL 17 rehearsal

`tools/verify_ticket_printer_schema.sh` applies the historical migrations to a
disposable PostgreSQL 17 database in the local Supabase lab. It never resets
the Local-Delivery database and drops the disposable database on exit.

The verified application contract is:

- 38 application schema migrations;
- 12 public tables, all with RLS;
- 53 public RLS policies;
- eight public functions; and
- five application triggers.

The 39th migration is infrastructure-specific. It installs `pg_cron` and
`pg_net`, then schedules `loadrite-sync` against the managed Supabase URL.
That migration is intentionally excluded from the isolated schema rehearsal
and must not be applied to the self-hosted application database.

## Historical Auth coupling

Two historical migrations directly reference three managed Auth user UUIDs.
That is valid history but is not a portable bootstrap mechanism.

The disposable rehearsal creates placeholder Auth rows for those UUIDs solely
to prove the remaining schema chain. No real email, password, identity,
metadata, or production user row is copied into the lab.

The cutover baseline must separate:

1. schema creation, with no user-specific seed rows;
2. managed Auth export and import, preserving user UUIDs;
3. role import after the corresponding Auth users exist; and
4. a one-time initial-administrator bootstrap for a genuinely blank install.

Existing users will need to re-authenticate after the signing-key cutover.
Passwords and identities must be migrated through the supported Auth export
path rather than reconstructed from application tables.

## Edge Functions

All 17 deployed functions were compared with local source:

- 16 match exactly;
- `loadrite-sync` differs by one behavior;
- the deployed function uses the accumulated `currentNote` for a completed
  Loadrite group, while local source prefers `rec.UserData3` when present.

The normalized deployed `loadrite-sync` source is retained under
`baselines/ticket-printer/functions/loadrite-sync`.

`tools/verify_ticket_printer_edge_functions.sh` temporarily mounts Ticket
Printer functions in the local Edge Runtime with all external credentials
empty. It verifies:

- Google address lookup refuses to call externally without its key;
- Loadrite and Loadrite sync refuse to call externally without credentials;
- all three Resend functions refuse to call externally without a key;
- protected agent and account functions reject missing authentication; and
- no Edge Runtime worker failure occurs.

The verifier always restores the Local-Delivery function mounts on exit.

## Loadrite scheduling

The self-hosted replacement for Supabase `pg_cron` is:

- `ops/ticket-printer/ghos-ticket-printer-loadrite-sync`;
- `ghos-ticket-printer-loadrite-sync.service`; and
- `ghos-ticket-printer-loadrite-sync.timer`.

The timer invokes only a loopback Edge Function URL every five minutes. Its API
key lives in `/etc/ghos-ticket-printer/loadrite-sync.env`, outside Git, with
root-only permissions. Production installation remains gated until the
self-hosted Ticket Printer Supabase stack and secret injection are ready.

## Client cutover configuration

Migration branch `codex/self-hosted-supabase-config` in
`/Users/mattstengel/edit-my-ticket` removes the managed project URL and
publishable key from browser source. The browser now requires
`VITE_SUPABASE_URL` and `VITE_SUPABASE_PUBLISHABLE_KEY` at build time. The
local `loadrite-sync` candidate also requires `SUPABASE_URL` instead of falling
back to the managed project.

The branch includes a tracked, placeholder-only `.env.example`; real
environment files remain ignored. A production build and all four existing
tests pass with deterministic local test configuration. The pre-existing
`Reports.tsx` working-tree change was preserved and was not included in the
migration commit.

Run:

```bash
tools/verify_ticket_printer_client_config.sh
```

The verifier rejects managed project URLs in tracked browser or Edge Function
runtime source. The historical cron migration still contains the managed URL
as immutable history and remains excluded from self-hosted deployment.

## Storage and Realtime static inventory

The corrected secret-safe source inventory finds no Ticket Printer
`storage.from(...)`, Realtime channel, or `postgres_changes` usage in
authoritative source, and no Storage bucket creation in its application
migrations. Generated Capacitor `public/assets` bundles are excluded so an old
mobile build cannot be mistaken for current source configuration.

This narrows the expected cutover contract, but it does not prove that the
managed project has no orphaned Storage objects or database-side Realtime
publication settings. Those remain read-only live-export checks.

## Remaining gates

- Obtain an exact production database/Auth export without resetting managed
  database passwords.
- Reconcile the managed schema and row counts against the local contract.
- Confirm the static no-Storage/no-Realtime finding against the managed project.
- Inject secret values through root-only runtime configuration.
- Test Loadrite, Resend, Google, and agent callbacks using approved test
  endpoints.
- Run an Auth/session acceptance test with disposable users.
- Perform a backup and restore drill before any production cutover.
