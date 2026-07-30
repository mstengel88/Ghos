# Local-Delivery data reconciliation plan

Status: exact encrypted-snapshot classification, owner quote disposition, and
notification-only clone merge rehearsal complete; final delta rehearsal
pending

Canonical target: Local-Delivery / Dispatch V2 Sandbox

Legacy comparison source: GreenHills Quote Live

Production access remains read-only. No customer rows, Auth identities, Storage
objects, Shopify sessions, or credentials belong in Git.

## Ownership decision

Dispatch V2 Sandbox already contains the continuing dispatch and quote
workflows. The Local-Delivery project therefore owns the future operational
model. Quote Live remains a protected source until its unique quote and legacy
dispatch records have been reconciled and archived.

| Data group | Canonical owner | Reconciliation rule |
|---|---|---|
| Dispatch orders, routes, trucks, employees | Local-Delivery | Local current state wins; preserve legacy-only history |
| Stop metrics and driver locations | Local-Delivery | Preserve metrics; archive stale location samples |
| Dispatch audit and Shopify updates | Local-Delivery | Append legacy-only IDs; never rewrite canonical events |
| Notifications | Local-Delivery | Preserve legacy-only records only when referenced entities exist |
| Custom delivery quotes | Local-Delivery V2 quote workflow | Merge by quote UUID; quarantine conflicting shared fields |
| B2B companies | Local-Delivery | Local shape and values are authoritative |
| Product source map | Shopify refresh through Local-Delivery | Compare by SKU, then refresh rather than importing stale rows blindly |
| Origins, material rules, app settings | Local-Delivery | Compare natural keys and require manual approval for conflicts |
| App profiles and roles | Local-Delivery Auth | Reconcile people by normalized email, never UUID alone |
| Shopify sessions and app tokens | Neither database copy | Reauthorize in the target environment |
| Push subscriptions | Neither database copy | Devices re-register after cutover |
| Dispatch photos | Local-Delivery | Transfer every object and metadata plus embedded database images; Quote Live has no objects |

## Reconciliation classifications

Every source row is loaded into the local-only
`migration_reconcile.source_rows` staging table as JSON:

- `canonical_only`: exists only in Local-Delivery;
- `legacy_only`: exists only in Quote Live;
- `matching`: same primary key and identical values for fields shared by both
  project shapes;
- `conflict`: same primary key but different shared values.

Nothing in `conflict` is auto-merged. A reviewed decision is recorded in
`migration_reconcile.merge_decisions` before a production migration can be
generated.

The documented ownership policy is encoded by
`seed_reconciliation_policy_decisions.sql`. It records 412 deterministic
decisions. `seed_owner_quote_disposition.sql` then records the owner's
direction to archive the three legacy-only quotes and keep the canonical side
of the one quote-total conflict. No Quote Live quote is imported, no Auth
identity map is required for this selected merge, and neither script writes to
production.

The classification and decision rules are exercised repeatably by
`migration/supabase/sql/verify_reconciliation_classification.sql`. The test
uses synthetic transaction-only rows, verifies both source manifests, and
rolls back without retaining staged data. It also verifies that a reviewed
legacy Auth UUID is rewritten to its canonical UUID and that an unmapped quote
owner remains quarantined.

## Identity rules

Auth is reconciled separately from public rows:

1. Normalize email with `lower(trim(email))`.
2. Keep the Local-Delivery Auth UUID for an existing person.
3. Map Quote Live UUIDs to the canonical UUID through an explicit identity map.
4. Create or invite Quote-only users through the target Auth service.
5. Rewrite Auth foreign keys only from the reviewed identity map.
6. Do not migrate active sessions, refresh tokens, JWTs, or service keys.

The current count differences—13 Auth users, 12 app profiles, and 11 dispatch
roles in Local-Delivery—must be explained by UUID before cutover.

The approved mapping is loaded only into the private local
`migration_reconcile.identity_map` table. The
`migration_reconcile.quote_import_candidates` view is the import boundary:
only rows with `ready_for_import = true` may feed a generated quote migration.
An unmapped creator never falls back to another user or to a null owner.

Create the private identity-map review packet with:

```bash
./tools/create_private_identity_reconciliation_review.sh
```

The command repeats the exact signed restore and policy checks, then creates an
owner-only HTML comparison and blank CSV approval worksheet under the
Git-ignored `migration/supabase/secrets/identity-review/` directory. It asserts
11 normalized-email candidates, two UUID rewrites, and 39 Quote Live creator
references before producing the packet. The disposable databases are removed
on success or failure.

## Table-specific merge rules

### Current dispatch state

For duplicate IDs in `dispatch_orders`, `dispatch_routes`,
`dispatch_employees`, and `dispatch_trucks`, Local-Delivery wins. Legacy-only
records are imported only after their foreign keys are mapped and their status
is confirmed as historical rather than active work.

No Quote Live row may overwrite current route assignment, stop sequence,
delivery window, travel time, proof-of-delivery, or delivery status in
Local-Delivery.

### Quotes

`custom_delivery_quotes.id` is the identity key. Quote-only UUIDs are candidates
for import into the newer 38-column Local-Delivery shape. Newer fields absent
from Quote Live use target defaults or remain null. Duplicate UUIDs with
different shared values require review; timestamps alone do not justify an
automatic overwrite.

### Configuration

- `app_settings`: compare by `key`;
- `shipping_material_rules`: compare by `prefix`;
- `product_source_map`: compare by `sku`;
- `shopify_app_settings`: compare by `shop`;
- `origin_addresses`: compare IDs and normalized label/address.

Configuration conflicts are resolved manually, then verified through the quote
calculator, material calculators, Shopify rates, and Dispatch V2.

### Append-only history

Audit and Shopify-update records are merged by their primary key. Duplicate IDs
must match; a differing duplicate is quarantined. Rows with broken references
are retained in an encrypted archive until a mapping decision is made.

## Export and handling boundary

Bulk production transfer requires encrypted logical exports or a direct
read-only database connection. MCP is appropriate for inventory and aggregate
verification, but not for a complete 38,000-row audit history, Auth migration,
or 610.7 MiB Storage transfer.

The first privacy-preserving key/row fingerprint comparison is recorded in
`sanitized-reconciliation-20260728.md`. It confirmed that Quote Live has no
legacy-only orders, routes, trucks, or stop metrics. It identified 40
legacy-only read notifications with valid canonical order/route references and
three legacy-only quotes requiring creator-identity review.

The exact encrypted-snapshot comparison is recorded in
`exact-reconciliation-20260730.md`. It verified all 23 Local-Delivery and 22
Quote Live public tables, including Quote Live's empty B2B table. The exact
comparison found 44,434 canonical-only, 43 legacy-only, 1,489 matching, and 373
raw conflicting records. After excluding reviewed timestamp, environment, and
identity/source-UUID differences, the main decision set is 163 operational
order conflicts, 28 product-map conflicts, 10 route conflicts, seven truck
conflicts, seven location conflicts, two profile conflicts, one stop-metric
conflict, and one quote-total conflict.

The exact policy rehearsal resolves every non-quote review row plus the 16
duplicate quote records that differ only by creator UUID. It creates 371
`keep_canonical`, 40 `import_legacy`, and one
`exclude_environment_state` decision. The owner disposition then archives the
three legacy-only quotes and keeps the existing canonical row for the one
duplicate quote with a differing total. No Quote Live quote is imported.

Create the private customer-level review packet with:

```bash
./tools/create_private_quote_reconciliation_review.sh
```

The command repeats both signed restore rehearsals, verifies the exact 412/4
boundary, writes a local-only HTML comparison and CSV decision worksheet under
the Git-ignored `migration/supabase/secrets/reconciliation-review/` directory,
and removes both disposable databases. The packet contains customer data and
must not be committed, emailed, or pasted into chat.

Rehearse the selected notification-only merge with:

```bash
./tools/rehearse_local_delivery_quote_merge.sh
```

The disposable-clone rehearsal inserted the 40 verified legacy notifications,
left `custom_delivery_quotes` at 212 rows and `dispatch_orders` at 970 rows,
changed no non-notification table count, and removed both temporary databases.
No production database was written.

The read-only photo inventory is recorded in
`storage-manifest-20260728.md`. At inventory time the bucket held 470 objects,
while 170 orders retained roughly 151.4 million characters of embedded JPEG
data in `dispatch_orders.photo_urls`. Both storage forms must migrate.

When database access is available:

1. Create fresh encrypted exports for both projects.
2. Record checksums and exact extraction timestamps.
3. Keep exports under the ignored `migration/supabase/exports/` path.
4. Load JSON row projections into the local reconciliation schema.
5. Run the classification and conflict reports.
6. Generate a reviewed, deterministic merge migration.
7. Rehearse the merge against a newly restored Local-Delivery target.

## Acceptance gates

- [x] Exact production exports retained and checksummed
- [x] All 22 Quote Live tables classified by primary/natural key
- [x] Every legacy-only or conflicting row has a deterministic policy or owner
      disposition
- [x] Auth identity map is not required because no Quote Live quote or creator
      identity crosses the selected merge boundary
- [ ] Foreign-key orphan report is empty
- [x] Active order and route rows remain unchanged in the disposable merge
- [x] Quote rows remain unchanged and no Quote Live quote is imported
- [ ] Product and material calculator configuration tested
- [x] Every Storage object in the current checkpoint restored with matching
      SHA-256
- [ ] Embedded and unclassified `dispatch_orders.photo_urls` payloads retained
- [ ] Final delta rehearsal passes after a simulated write freeze
- [ ] Rollback restore tested
