# Ticket Printer migration

Last verified: 2026-07-28

Ticket Printer currently uses managed project `dlayrpnmfnbjlxgnkczv`. Its
primary local source is `/Users/mattstengel/edit-my-ticket`.

## PostgreSQL 17 rehearsal

`tools/verify_ticket_printer_schema.sh` applies the historical migrations to a
disposable PostgreSQL 17 database in the local Supabase lab. It never resets
the Local-Delivery database and drops the disposable database on exit.

The checked-in historical migration contract is:

- 38 application schema migrations;
- 12 public tables, all with RLS;
- 53 public RLS policies;
- eight public functions; and
- five application triggers.

Read-only Supabase MCP inspection of the managed project found two additional,
empty dispatch-bridge tables that are not present in that history:

- `dispatch_orders`
- `dispatch_routes`

The live managed contract is therefore:

- 14 public tables, all with RLS;
- 53 public RLS policies;
- ten public functions; and
- seven application triggers.

Both dispatch tables have RLS enabled and no policies, so normal browser roles
cannot access them. The local candidate at
`migration/supabase/candidates/ticket-printer/000_live_dispatch_bridge.sql`
preserves that privileged-service-only contract. It is applied only to the
disposable PostgreSQL 17 rehearsal and has not been written to the managed
project.

The 39th migration is infrastructure-specific. It installs `pg_cron` and
`pg_net`, then schedules `loadrite-sync` against the managed Supabase URL.
That migration is intentionally excluded from the isolated schema rehearsal
and must not be applied to the self-hosted application database.

## Clean-room API recovery rehearsal

`tools/verify_ticket_printer_api_recovery.sh` now clones the local Supabase
platform database, replaces only the clone's application schema with Ticket
Printer, and temporarily activates that clone for API acceptance. Before the
swap it creates a private safety dump. A cleanup trap restores the canonical
Local-Delivery database and all local Supabase services on success or failure.

The rehearsal verifies:

- the 14-table live schema contract and RLS on every public table;
- service-role PostgREST access;
- administrator creation of a disposable Auth user;
- password login through GoTrue;
- automatic profile and default-role provisioning;
- authenticated, RLS-filtered access to the new user's own profile; and
- deletion of the disposable user.

The first clean-room run exposed a platform dependency absent from the
historical application migrations: managed Supabase supplies Data API grants
outside that migration chain. Without equivalent grants, PostgREST returned
`403` even to the local service role. The reviewed candidate
`migration/supabase/candidates/ticket-printer/001_api_grants.sql` now recreates
the standard schema/table/sequence/function grants and default privileges.
Because all 14 public tables have RLS enabled, those privileges allow policies
to be evaluated; they do not independently grant browser roles row access.
The two dispatch bridge tables still have no browser policies and remain
service-role-only.

The complete recovery rehearsal passed on 2026-07-29, after which the
Local-Delivery lab was restored with its 22-table, 26-policy contract intact.

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

Read-only managed-project inspection now confirms:

- zero Storage buckets and zero Storage objects; and
- no tables in the `supabase_realtime` publication.

This agrees with the static inventory and closes the Storage/Realtime discovery
gate for the current project state.

## Managed-project inventory

The 2026-07-28 read-only inventory recorded:

- PostgreSQL 17.6;
- approximately 31.9 MB total database size;
- 39 managed migration-history entries;
- nine Auth users and nine Auth identities;
- 58 Auth sessions;
- one active five-minute cron job; and
- 17 active Edge Functions.

No row contents, Auth secrets, cron command, or Edge Function secret values
were retrieved.

Public table row counts:

| Table | Rows |
| --- | ---: |
| `agent_registry` | 2 |
| `audit_logs` | 1,091 |
| `customers` | 67 |
| `dispatch_orders` | 0 |
| `dispatch_routes` | 0 |
| `feedback` | 17 |
| `orders` | 12 |
| `products` | 92 |
| `profiles` | 9 |
| `template_versions` | 48 |
| `ticket_templates` | 3 |
| `tickets` | 718 |
| `trucks` | 9 |
| `user_roles` | 9 |

The Supabase security advisors also flag pre-existing issues that must be
reviewed before cutover:

- several mutable function `search_path` settings;
- overly permissive RLS policies on existing application tables; and
- `SECURITY DEFINER` functions executable by broad API roles.

The dispatch candidate fixes only its own two function search paths. Existing
authorization behavior is not being changed during inventory capture.

## Remaining gates

- Obtain an exact production database/Auth export without resetting managed
  database passwords.
- Reconcile the managed schema and row counts against the local contract.
- Design and approve any RLS/function privilege hardening separately from the
  fidelity migration.
- Inject secret values through root-only runtime configuration.
- Test Loadrite, Resend, Google, and agent callbacks using approved test
  endpoints.
- Run an Auth/session acceptance test with disposable users.
- Perform a backup and restore drill before any production cutover.
