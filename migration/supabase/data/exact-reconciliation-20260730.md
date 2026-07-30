# Local-Delivery and Quote Live exact reconciliation

Date: 2026-07-30 UTC

Status: exact encrypted snapshots loaded, classified, owner disposition
recorded, and the selected notification-only merge rehearsed successfully in
an isolated clone; no production writes

Canonical target: Local-Delivery / Dispatch V2 Sandbox

Legacy source: GreenHills Quote Live

The report contains aggregate counts only. Customer rows, identifiers, Auth
records, delivery photos, and credentials remain exclusively in encrypted,
Git-ignored exports and disposable local databases.

## Verified source manifests

| Source | Public tables | Notable verified rows |
|---|---:|---|
| Local-Delivery | 23 | 970 orders, 212 quotes, 475 Storage objects |
| Quote Live | 22 | 457 orders, 89 quotes, 0 Storage objects |

Quote Live's empty `dispatch_b2b_companies` table is represented explicitly in
the source-table manifest. Local-Delivery's additional table is
`quote_tax_rate_cache`.

Both encrypted database archives passed checksum verification, full disposable
PostgreSQL 17 restore verification, Auth/Storage relationship checks, and exact
signed relation-count checks before reconciliation.

## Exact classification

| Classification | Records |
|---|---:|
| Canonical only | 44,434 |
| Legacy only | 43 |
| Matching | 1,489 |
| Conflict | 373 |

The 43 legacy-only records are 40 read dispatch notifications and three custom
delivery quotes. No Quote Live order, route, truck, employee, stop-metric,
product-map, audit, or Shopify-update record is legacy-only.

## Exact table summary

| Table | Canonical only | Legacy only | Matching | Raw conflicts |
|---|---:|---:|---:|---:|
| `app_audit_log` | 208 | 0 | 622 | 36 |
| `app_settings` | 0 | 0 | 16 | 0 |
| `app_user_profiles` | 1 | 0 | 9 | 2 |
| `custom_delivery_quotes` | 126 | 3 | 69 | 17 |
| `dispatch_audit_log` | 42,824 | 0 | 287 | 0 |
| `dispatch_b2b_companies` | 82 | 0 | 0 | 0 |
| `dispatch_driver_locations` | 3 | 0 | 1 | 7 |
| `dispatch_employees` | 1 | 0 | 5 | 6 |
| `dispatch_notifications` | 25 | 40 | 20 | 0 |
| `dispatch_orders` | 513 | 0 | 294 | 163 |
| `dispatch_push_subscriptions` | 0 | 0 | 2 | 0 |
| `dispatch_routes` | 2 | 0 | 12 | 10 |
| `dispatch_settings` | 0 | 0 | 2 | 0 |
| `dispatch_shopify_updates` | 116 | 0 | 40 | 0 |
| `dispatch_stop_metrics` | 502 | 0 | 89 | 1 |
| `dispatch_trucks` | 1 | 0 | 0 | 7 |
| `dispatch_user_roles` | 10 | 0 | 1 | 0 |
| `origin_addresses` | 0 | 0 | 5 | 0 |
| `product_source_map` | 16 | 0 | 5 | 123 |
| `quote_tax_rate_cache` | 4 | 0 | 0 | 0 |
| `Session` | 0 | 0 | 0 | 1 |
| `shipping_material_rules` | 0 | 0 | 9 | 0 |
| `shopify_app_settings` | 0 | 0 | 1 | 0 |

## Normalized conflict findings

Volatile timestamps, environment-specific Shopify session values, reviewed
identity UUID fields, and source-row UUIDs for natural-keyed product/profile
records were excluded only from this explanatory classification. The raw
records remain unchanged in staging.

| Table | Substantive | Explainable metadata/identity only |
|---|---:|---:|
| `app_audit_log` | 0 | 36 |
| `app_user_profiles` | 2 | 0 |
| `custom_delivery_quotes` | 1 | 16 |
| `dispatch_driver_locations` | 7 | 0 |
| `dispatch_employees` | 0 | 6 |
| `dispatch_orders` | 163 | 0 |
| `dispatch_routes` | 10 | 0 |
| `dispatch_stop_metrics` | 1 | 0 |
| `dispatch_trucks` | 7 | 0 |
| `product_source_map` | 28 | 95 |
| `Session` | 0 | 1 |

Important field-level findings:

- the 16 quote conflicts outside the single total mismatch are creator UUID
  differences;
- 158 order conflicts include different `photo_urls`, so embedded proof data
  must remain canonical and must never be overwritten;
- 27 orders differ in delivery/status state and 26 include delivery lifecycle
  evidence such as checklist, delivered/departed timestamps, or proof name;
- product-map substantive differences include 18 unit labels, six records in
  each contractor price tier, three retail prices, and isolated vendor/image/
  variant differences;
- all six employee conflicts are timestamp-only;
- all 36 app-audit conflicts are identity-UUID-only.

## Identity gates

| Gate | Result |
|---|---:|
| Profile email overlap | 11 |
| Profile UUID rewrites required | 2 |
| Quote creators resolvable by normalized email | 39 |
| Quote creators unmapped | 0 |
| Quote creator not present/not required | 50 |

The owner directed that no Quote Live quote be imported. The three legacy-only
quotes remain only in the encrypted rollback archive, and the legacy side of
the duplicate quote-total conflict is discarded while the existing canonical
Local-Delivery quote remains unchanged. No identity rewrite is therefore
required for the selected merge.

## Reconciliation policy consequence

- Local-Delivery wins every current order, route, truck, employee, location,
  stop-metric, and proof-of-delivery conflict.
- Quote Live's 40 legacy-only read notifications may be imported only after
  their referenced canonical entities are reverified.
- The three legacy-only quotes are archived and are not imported.
- The existing Local-Delivery row wins the one duplicate quote-total conflict;
  the Quote Live version is not imported.
- Product maps must be refreshed from Shopify rather than copied from Quote
  Live.
- Shopify sessions and push subscriptions are environment state and are not
  migrated.

## Deterministic decision boundary

The documented ownership policy now produces 412 reviewable decisions directly
from the exact snapshots:

| Decision | Records |
|---|---:|
| Keep canonical Local-Delivery row | 371 |
| Import verified legacy notification | 40 |
| Exclude environment-specific Shopify session | 1 |

The 371 canonical decisions include the 16 duplicate quotes whose only shared
field difference is the creator UUID. They retain the canonical quote and do
not rewrite business values.

The original fail-closed human review queue contained exactly four records:

| Quote classification | Records |
|---|---:|
| Legacy-only quote | 3 |
| Duplicate quote with a differing total | 1 |

The owner-disposition script records `archive_legacy` for the three
legacy-only quotes and `keep_canonical` for the duplicate conflict. It refuses
to run unless the exact 412/4 baseline is present and then verifies that all
four decisions exist, no unresolved row remains, and no quote is eligible for
import. The legacy records are retained in the encrypted rollback archive; no
live production row is deleted.

The policy and merge scripts also refuse notification imports with invalid
canonical references. Auth identity candidates remain private and are not
loaded because the selected merge imports no quote or creator identity.

## Disposable merge rehearsal

The selected merge was applied to a newly restored disposable
Local-Delivery clone and verified:

| Result | Count |
|---|---:|
| Verified legacy notifications inserted | 40 |
| Final notifications | 85 |
| Final quotes | 212 |
| Final dispatch orders | 970 |
| Quote imports | 0 |
| Non-notification tables with changed row counts | 0 |

Every inserted notification matched the exact staged source JSON, all
constraints passed, and the canonical quote count remained unchanged. Both
temporary databases were removed after the rehearsal.

## Reproduction

Run:

```bash
./tools/reconcile_local_delivery_quote_live_snapshots.sh
```

The command restores both encrypted snapshots into disposable databases,
verifies every source-table manifest, streams JSON projections directly between
local PostgreSQL processes, seeds the documented deterministic decisions,
prints aggregate reports only, verifies the exact decision boundary, and drops
the temporary databases on exit.

Rehearse the owner-approved notification-only merge against a disposable clone:

```bash
./tools/rehearse_local_delivery_quote_merge.sh
```

This command restores and verifies both exact snapshots, applies the
owner-directed quote disposition, imports only the 40 verified notifications,
checks that no quote or other table count changed, and removes both disposable
databases on exit.
