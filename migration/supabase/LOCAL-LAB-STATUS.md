# Local Supabase compatibility lab status

Last verified: 2026-07-28

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
after the functions were mounted: 22 tables, 26 policies, eight functions,
eight triggers, 106 indexes, 45 constraints, Storage metadata, Realtime
publication, and all synthetic reconciliation classifications.

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
lab without production rows, Auth identities, secrets, or Storage objects. The
rehearsal matches the managed project contract:

- 22 public tables, all with RLS enabled;
- 26 public policies;
- eight public functions and eight active application triggers;
- 106 public indexes;
- exact column names, types, nullability, and defaults for all 22 tables;
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
