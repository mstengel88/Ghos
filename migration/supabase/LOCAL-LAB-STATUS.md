# Local Supabase compatibility lab status

Last verified: 2026-07-30

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

All 12 application services are running and healthy:

- Auth
- PostgreSQL
- Edge Functions
- imgproxy
- Kong
- Mailpit
- postgres-meta
- Realtime
- PostgREST
- Storage
- Studio
- Supavisor

## Local-Delivery Edge Function compatibility

The exact deployed `carrier-service`, `shopify-api`, and shipping-calculator
sources are now retained under the tracked Local-Delivery baseline with
SHA-256 hashes. A local-only Compose override mounts them into the Edge Runtime
while forcing Shopify, Google, and administrator credentials to empty values.

`tools/verify_local_delivery_edge_functions.sh` passed:

- all three captured-source hash checks;
- carrier-service CORS preflight;
- the non-POST and missing-rate empty-rate contracts;
- the shopify-api missing-configuration guard;
- verification that no external Shopify call is attempted without credentials.

The complete Local-Delivery schema, RLS, and reconciliation suite passed again
after the functions were mounted and the current tax-cache migration was
promoted: 23 tables, 26 policies, eight functions, eight triggers, 109 indexes,
47 constraints, Storage metadata, Realtime publication, and all synthetic
reconciliation classifications.

The exact Local-Delivery/Quote Live reconciliation policy rehearsal also
passes. It verifies both full source manifests and records 412 deterministic
ownership decisions. The owner directed that the three legacy-only quotes be
archived without import and that the canonical Local-Delivery row win the one
quote-total conflict. The selected merge therefore requires no identity map
and imports no Quote Live quote. No production database is written.

The self-hosted baseline now also declares the standard Data API grants that
managed Supabase normally installs outside application migrations. A
disposable PostgreSQL 17 rebuild passed with 22 tables, 26 policies, and the
expected anonymous, authenticated, and service-role object privileges. RLS
remains enabled on all 22 tables. The running lab then passed password-session,
invitation, password-recovery, RLS, Storage bucket, and Realtime acceptance.

After production drift discovery, the tracked
`002_quote_tax_rate_cache.sql` candidate advanced the standing lab to 23
RLS-enabled tables. The cache has no client policies: anonymous and
authenticated test identities see zero rows, while the server-side
`service_role` can manage it. The clean-room contract/RLS/reconciliation suite
and Quote Live compatibility test both passed after promotion.

The guarded full reset correctly refused to erase the standing lab because its
Storage bucket currently contains objects. Those objects were preserved. A
pre-change custom-format database backup remains at:

```text
/tmp/ghos-local-delivery-before-tax-cache-20260730T010943Z.dump
SHA-256: 041aa345674028f7f0f80de52a5c67969ef069a9b1a723338e27609b5fae3663
```

The captured carrier callback has a blocking scope defect: its route helper
references `RATE_PER_MINUTE`, but that value is currently declared only inside
the request handler. The deployed source remains unchanged as evidence. A
reviewed candidate and mocked Google/Shopify acceptance are required before
external callback cutover.

A separate reviewed candidate now corrects that scope defect without changing
the captured evidence. Its unit and callback acceptance tests passed against a
temporary localhost-only route mock:

- 15 minutes and 10 miles one way;
- 30 calculated round-trip minutes;
- `$2.08` per minute;
- `$62.40` returned as the Shopify carrier rate;
- negative inputs clamped to zero.

The candidate retains Google and Shopify's official endpoints as defaults;
only the local Compose override supplies mock URLs. The expanded acceptance
suite now also passed:

- vendor-specific origin selection for two Shopify variants;
- two unique cached routes across four required truck loads;
- a combined carrier rate of `$291.20`;
- rejection of a route beyond the configured 50-mile limit;
- Shopify client-credential exchange and product transformation;
- a deterministic Shopify shipping quote of `$172.50`;
- safe propagation of a synthetic Shopify GraphQL error.

All external requests stayed on localhost. Disposable origin rows were removed,
the complete Local-Delivery contract/RLS/reconciliation suite passed, and the
Edge Functions container was returned to the captured secret-free baseline.
Callback authentication, credential injection, public HTTPS, and registration
remain required before callback cutover.

## Application endpoint configuration

Dispatch V2 already reads its Supabase URL, anonymous key, and service-role key
from server runtime variables. No managed project URL or JWT-shaped key was
found in its tracked application source outside ignored environment files.

ShipCalc's `codex/self-hosted-supabase-config` branch now reads its Supabase base
URL and publishable key from Vite environment variables for both the browser
client and every Edge Function request. Its test and production build passed.
The branch also stops tracking `.env` while preserving the local file.

See `APPLICATION-CONFIGURATION.md`. Repository-history review and rotation of
any previously committed restricted credential remain required; no database
password was reset.

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

## WinterWatch production restore rehearsal

The encrypted 2026-07-29 production database/Auth export restored successfully
to an isolated clone in the pinned PostgreSQL 17 compatibility lab. The
existing Local-Delivery `postgres` database was not reset or overwritten.

Verification passed:

- all expected rows across 20 public tables;
- 20 of 20 public tables with RLS and 74 policies;
- 12 Auth users, 13 identities, and zero orphan identities;
- 12 profiles and zero orphan profiles;
- one Storage bucket and 92 object metadata rows;
- zero invalid or unready indexes; and
- zero unvalidated constraints.

The signed export remains unchanged. A rehearsal-only schema copy skips
`pg_cron`, which cannot be installed in a clone database, and a rehearsal-only
data copy removes the empty managed-only
`auth.custom_oauth_providers.custom_claims_allowlist` COPY header column. The
tool aborts if that table contains any rows. See `projects/winterwatch-pro.md`
and:

```bash
tools/rehearse_winterwatch_restore.sh
tools/verify_winterwatch_restore.sh
```

Full local API recovery acceptance also passed on 2026-07-29. The guarded
rehearsal temporarily activated the isolated WinterWatch clone, then verified:

- PostgREST access to the restored application schema;
- an administrator Auth create, read, and delete lifecycle;
- the restored private `work-photos` bucket; and
- an authenticated object download whose SHA-256 matched the private export.

All 92 private objects were mapped to their restored Storage object versions,
copied into the Docker named volume, assigned the file backend's required
content-type and cache-control extended attributes, and verified before the API
test. The Local-Delivery compatibility database and all 12 services were
restored automatically after the rehearsal. Repeat with:

```bash
tools/import_winterwatch_storage_local.sh
tools/verify_winterwatch_api_restore.sh
```

## Dump Site isolated schema rehearsal

The eight canonical Dump Site migrations now apply cleanly to a disposable
database inside the pinned Supabase PostgreSQL 17 container. The rehearsal does
not reset or modify the Local-Delivery `postgres` database and removes its
temporary database on exit.

`tools/verify_dump_site_schema.sh` passed:

- three service-only public tables with RLS;
- 32 `dump_site_entries` columns;
- nine application functions and two triggers;
- first generated order number `201-D10000`;
- automatic CounterPoint queueing when Modern Retail is disabled;
- queue claim and completion behavior; and
- rate-limit increment behavior.

Exact managed-schema comparison and read-only aggregate production inventory
are complete. Production row payloads, secret values, and external callbacks
remain gated. See `projects/dump-site.md`.

## Dump Site clean-room API recovery

The Dump Site candidate now passes a full local API recovery rehearsal. The
guarded verifier:

- creates a private safety dump and disposable clone of the Supabase platform
  database;
- applies all eight canonical migrations;
- validates the three-table, three-RLS-table, zero-policy service-only
  contract;
- confirms anonymous/browser access is denied;
- creates a synthetic service-role submission through PostgREST;
- verifies generated confirmation `201-D10000`;
- claims and completes the CounterPoint bridge entry through its service-role
  RPCs; and
- deletes the fixture and restores Local-Delivery plus all local services
  automatically.

The successful 2026-07-29 rehearsal was followed by an independent check that
Local-Delivery was restored with 22 public tables and 26 policies. No temporary
candidate databases remained. Edge Function authorization and synchronized
iOS/Android endpoint checks also passed afterward.

## Ticket Printer isolated compatibility rehearsal

Thirty-eight Ticket Printer application migrations apply cleanly to a
disposable PostgreSQL 17 database. The resulting contract contains 12 public
tables, RLS on all 12, 53 policies, eight functions, and five triggers.

The historical migrations require three managed Auth UUIDs to exist before
user-role seeds run. The verifier supplies placeholder Auth rows only inside
the disposable database. The consolidated self-hosted baseline must separate
schema creation from Auth and role import.

The remaining migration installs `pg_cron` and schedules a managed-project
Edge Function URL. It is intentionally excluded from the application schema
rehearsal and has a GHOS systemd service/timer replacement.

All 17 deployed Ticket Printer Edge Functions were reconciled. Sixteen match
local source; deployed `loadrite-sync` differs in completed-group note
selection and is retained as the migration baseline. Secret-free local
acceptance passed for Loadrite, Google, Resend, agent, and account guards.
The Local-Delivery Edge Function mounts were restored and reverified after the
test.

Production rows, Auth identities, secret values, and external callbacks remain
gated. See `projects/ticket-printer.md`.

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
  -f ../../docker-compose.mailpit.yml \
  up -d
```

Stop the containers without deleting the database or Storage volumes:

```bash
docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  -f ../../docker-compose.mailpit.yml \
  stop
```

Do not use `down --volumes` after migration data has been loaded.

## Local Auth email compatibility

`docker-compose.mailpit.yml` adds the pinned Mailpit `v1.30.6` image and
redirects only the local Auth container's SMTP traffic to it. The review/API
interface binds to `127.0.0.1:8025`; SMTP remains internal to the Compose
network and no message is relayed externally.

Before enabling the override, a fresh schema-only backup was created:

```text
/tmp/ghos-local-delivery-before-mailpit-20260728-072935.sql
SHA-256: 18608413340742bb0d20a9a9f2b1ccf61282c1024164dfaa21dc64ec53512ea1
```

`tools/verify_local_delivery_auth_email.sh` passed invitation and
password-recovery acceptance end to end:

- invitation delivery, verification redirect, session issuance, and profile
  access;
- recovery delivery, verification redirect, session issuance, password
  replacement, and sign-in with the recovered password;
- deletion of both disposable users and captured messages.

Final cleanup showed zero `ghos-*@example.invalid` users, zero Storage objects,
and an empty Mailpit inbox. The Local-Delivery contract, RLS, and
reconciliation suite passed again afterward.

## Local-Delivery schema rehearsal

The read-only managed-project comparison is documented in
`projects/local-delivery.md`. Dispatch V2 Sandbox is the canonical continuing
dispatch runtime; the older dispatch implementation will be retired after data
reconciliation, while its quote tool remains in migration scope.

A schema-only Local-Delivery compatibility baseline has been restored into the
lab without production rows, Auth identities, secrets, or production Storage
objects. The rehearsal matches the current managed project contract:

- 23 public tables, all with RLS enabled;
- 26 public policies;
- eight public functions and eight active application triggers;
- 109 public indexes;
- exact column names, types, nullability, and defaults for all 23 tables;
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

## Repeatable clean-room acceptance

The guarded reset and complete schema rebuild were repeated successfully on
2026-07-27. The resulting database again matched all 22 table contracts, 26
RLS policies, eight functions, eight triggers, 106 indexes, 45 constraints,
Storage bucket metadata, and the Realtime publication.

The immediately preceding schema-only backup is:

```text
/tmp/ghos-local-delivery-clean-room-before-20260727-224158.sql
SHA-256: 04d6287baaf02161821b0416176378481020a909a48a3e0202faceef6bddb585
```

`tools/verify_local_delivery_clean_room.sh` provides repeatable contract,
transaction-only RLS, and synthetic reconciliation acceptance. It verifies
matching, canonical-only, legacy-only, and conflicting records; requires
reviewed decisions for the latter two actionable classifications; rolls back
all fixtures; and requires reconciliation staging to remain empty.

## Post-restore service acceptance

The pinned upstream self-hosted smoke suite was repeated after the
Local-Delivery schema restore on 2026-07-28 and passed 35 of 35 checks. This
confirmed:

- administrator user creation, password sign-in, authenticated profile access,
  and administrator deletion;
- anonymous and service-role PostgREST behavior;
- Storage bucket creation, a 7 MB upload, byte and hash matched download,
  signed URL access without authorization headers, and cleanup;
- multi-chunk TUS resumable upload, integrity verification, and cleanup;
- the baseline Edge Function, Realtime health, and protected administration
  routes.

Cleanup was verified independently after the suite:

```text
Auth users: 0
Storage objects: 0
Storage buckets retained: dispatch-photos
```

The complete Local-Delivery schema, RLS, and reconciliation suite passed again
after the service lifecycle test.

`tools/verify_local_delivery_auth_sessions.sh` then passed a guarded,
localhost-only Auth session acceptance flow covering administrator creation,
password sign-in, authenticated profile access, password replacement,
old-password rejection, logout, refresh-token rejection, administrator
deletion, and cleanup verification. Password recovery, invitations, and actual
application-specific Edge Functions remain separate acceptance gates because
the local stack does not yet include an email-capture service.

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

## Sanitized cross-project reconciliation

A read-only, privacy-preserving comparison of Local-Delivery and GreenHills
Quote Live completed through their project-scoped MCP connections. Keys and row
projections were SHA-256 hashed inside each managed database; only aggregate
classifications and field-difference counts were retained.

The pass confirmed:

- all 457 Quote Live dispatch orders already exist in Local-Delivery;
- no legacy-only routes, trucks, employees, or stop metrics exist;
- 40 legacy-only notifications are already read and retain valid canonical
  order and route references;
- three legacy-only quotes require an explicit creator-identity/archive
  decision;
- 17 overlapping quotes and two app profiles require review;
- configuration tables match, while product-source drift must be resolved by
  the canonical Shopify refresh.

No managed project was modified and no customer data or secret value was
exported. See
`data/sanitized-reconciliation-20260728.md`.

The privacy-preserving Auth comparison and canonical identity decisions are
recorded in `data/auth-identity-reconciliation-20260728.md`. Local-Delivery
identities are canonical; 16 Quote Live quote creator references require a
rehearsed UUID rewrite during import.

The clean-room reconciliation suite now exercises that rewrite path. A private
`migration_reconcile.identity_map` supplies reviewed legacy-to-canonical UUID
mappings. The `quote_import_candidates` view rewrites mapped owners, accepts
quotes that intentionally have no owner, and marks any unmapped owner as not
ready for import. The synthetic suite passed on 2026-07-28 and rolled back all
fixtures.

## Ticket Printer clean-room API recovery

The Ticket Printer candidate now passes a full local API recovery rehearsal,
not only a plain PostgreSQL schema test. The guarded verifier:

- creates a private safety dump and disposable clone of the Supabase platform
  database;
- applies 38 portable migrations plus the two live-contract candidates;
- validates the 14-table, 53-policy application contract;
- tests service-role PostgREST access, password Auth, profile and default-role
  provisioning, RLS-filtered profile access, and user deletion; and
- restores the Local-Delivery database and all local services automatically.

The rehearsal exposed and fixed the missing self-hosted Data API grants that
managed Supabase normally supplies outside application migrations. After the
successful 2026-07-29 run, Local-Delivery was independently confirmed restored
with 22 public tables and 26 policies.

## Quote Live consolidation compatibility

The continuing GreenHills Quote Live server workflow now has a local Data API
acceptance test against the newer Local-Delivery target:

```bash
tools/verify_quote_live_client_config.sh
tools/verify_quote_live_compatibility.sh
```

Both passed on 2026-07-29. The compatibility test covered quote
create/read/update/delete, product pricing and contractor tiers, vendor origins,
material rules, B2B terms, Shopify settings, RLS, and anonymous isolation. It
used unique local-only fixtures, removed them automatically, and verified zero
leftovers.

This supports consolidating the quote tool into the Local-Delivery-owned schema
without reviving Quote Live's legacy dispatch implementation. No production
customer row, Auth identity, Shopify token, or managed-project setting was
changed.

The guarded encrypted export and isolated restore paths are also prepared:

```bash
tools/export_greenhills_quote_live_database.sh
tools/rehearse_greenhills_quote_live_restore.sh
tools/verify_greenhills_quote_live_production_restore.sh
```

The restore verifier enforces Quote Live's 22-table RLS contract, 20 policies,
eight functions, eight triggers, Auth and Storage relationships, index and
constraint validity, and the 26-relation signed row-count manifest. The
generalized restore engine was regression-tested with the Local-Delivery
encrypted snapshot; the restore passed and the standing lab independently
passed its clean-room verification afterward.

Temporary access was enabled for Quote Live on 2026-07-29. A stale `aws-0`
shared-pooler hostname was then corrected to the project-verified `aws-1`
endpoint. No database password was reset.

The guarded export completed at:

```text
migration/supabase/exports/greenhills-quote-live/20260730T031508Z/
```

The approximately 57 MiB encrypted archive passed component and archive
checksum verification and remains excluded from Git. Its dedicated encryption
password remains in macOS Keychain.

The encrypted snapshot then passed the complete disposable PostgreSQL 17
restore rehearsal. Results included 22 RLS-enabled public tables, 20 policies,
eight functions, eight triggers, 11 Auth users and identities, 11 profiles, one
dispatch role, 89 quotes, 457 legacy dispatch orders, one empty Storage bucket,
all 26 exact relation counts, and no invalid indexes, unvalidated constraints,
or orphan Auth/Storage relationships. The disposable database and plaintext
files were removed, and the standing Local-Delivery lab passed independent
clean-room verification afterward with all services healthy.

## Local-Delivery encrypted database/Auth snapshot

Supabase temporary database access was enabled for the current operator and
the guarded read-only export completed on 2026-07-29:

```text
migration/supabase/exports/local-delivery/20260730T005031Z/
```

The approximately 187 MiB encrypted archive contains role settings, application
schema, database rows, Auth/Storage metadata, and exact reconciliation counts.
The exporter verified every plaintext component and the encrypted archive
checksum before removing plaintext work files. The encryption password remains
in macOS Keychain and the private export stays excluded from Git.

An independent encrypted off-host copy remains required before the backup gate
can be marked complete.

The encrypted production snapshot also passed a complete disposable restore
rehearsal on 2026-07-29. Archive and component checksums, schema, Auth, Storage
metadata, exact row counts, constraints, indexes, RLS, policies, functions,
triggers, and key relationships were verified. Results included 23
RLS-enabled public tables, 26 policies, 13 Auth users and identities, 970
orders, 212 quotes, and 475 Storage metadata rows. The disposable database and
plaintext working directory were removed automatically.

The restore exposed one valid production addition beyond the standing
22-table candidate lab: `public.quote_tax_rate_cache`, with four rows in the
signed snapshot. The future export count query now includes it. The existing
encrypted snapshot is handled deterministically by counting only that known
table's COPY block from the checksum-verified data dump before verification.

After the rehearsal, `tools/verify_local_delivery_clean_room.sh` independently
passed again against the unchanged standing lab.

## Exact Local-Delivery / Quote Live reconciliation

Both protected production snapshots were restored together into disposable
local PostgreSQL 17 databases and classified through:

```bash
tools/reconcile_local_delivery_quote_live_snapshots.sh
```

The runner now records a per-source table manifest, including empty tables,
before streaming JSON row projections directly between local database
processes. It does not write customer records or identifiers to disk or Git.
All 23 Local-Delivery tables, all 22 Quote Live tables, and every per-table row
count passed the exact staging gate.

Aggregate results were 44,434 canonical-only, 43 legacy-only, 1,489 matching,
and 373 raw conflicting records. The legacy-only set is limited to 40 read
notifications and three quotes. After removing explainable timestamp,
environment, identity, and source-row UUID differences from the explanatory
view, the product map has 28 substantive conflicts and the duplicate quote set
has one substantive total conflict. No Quote Live quote creator is unmapped in
the exact snapshot.

Full aggregate results and the resulting ownership decisions are documented in
`data/exact-reconciliation-20260730.md`. The selected notification-only merge
was rehearsed against a freshly restored disposable Local-Delivery clone: 40
verified notifications were inserted, notifications reached 85 rows, quotes
remained at 212, orders remained at 970, and no non-notification table count
changed. Both disposable databases were removed. Final delta rehearsal and
production cutover remain pending.

## Photo migration inventory

A read-only Storage/reference inventory found 470 current objects totaling
640,756,931 bytes. Database order records reference 452 distinct bucket
objects, leaving 18 currently unreferenced objects that must be preserved until
retention review.

Photo migration is not limited to Supabase Storage:

- 170 orders contain embedded JPEG data URLs in `dispatch_orders.photo_urls`;
- those embedded fields occupy 151,395,742 characters;
- four additional photo-text records are not yet classified.

The Storage metadata manifest, reference counts, and migration requirements are
recorded in `data/storage-manifest-20260728.md`. No object bytes or paths were
exported.

The writable source advanced after that inventory. On 2026-07-29, a fresh
private export captured 475 objects totaling 643,127,719 bytes. Local Storage
API restore acceptance uploaded five new objects, reused 470 byte-identical
objects, and downloaded all 475 back with zero SHA-256 mismatches. The export
was then rehashed, encrypted with AES-256-CBC/PBKDF2 using the existing
Keychain-held migration password, decrypted, opened, and checksum-verified.
Object paths, bytes, restore evidence, and the encrypted archive remain ignored
by Git. The remaining backup gate is an independent encrypted off-Mac copy.
