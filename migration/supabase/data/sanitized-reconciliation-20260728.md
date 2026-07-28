# Sanitized Local-Delivery reconciliation report

Run date: 2026-07-28

Canonical project: Local-Delivery / Dispatch V2 Sandbox

Legacy comparison project: GreenHills Quote Live

Status: privacy-preserving MCP comparison complete; exact encrypted exports and
the production merge are not started.

## Method

The comparison ran read-only SQL through each project-scoped Supabase MCP
connection. Primary keys, approved natural keys, and row projections were
SHA-256 hashed inside each managed database. Only aggregate classifications and
field-difference counts were retained.

No customer names, addresses, contact details, quote contents, photo paths,
Shopify sessions, Auth credentials, tokens, or secret values were exported.
This report is an inventory and merge-planning artifact, not a replacement for
the required encrypted logical backups.

## Key and row classifications

| Data set | Canonical | Legacy | Overlap | Matching | Conflicts | Canonical only | Legacy only |
|---|---:|---:|---:|---:|---:|---:|---:|
| App settings | 16 | 16 | 16 | 16 | 0 | 0 | 0 |
| Shopify app settings | 1 | 1 | 1 | 1 | 0 | 0 | 0 |
| Shipping material rules | 9 | 9 | 9 | 9 | 0 | 0 | 0 |
| Dispatch settings | 2 | 2 | 2 | 2 | 0 | 0 | 0 |
| Origin addresses | 5 | 5 | 5 | 5 | 0 | 0 | 0 |
| Product source map (SKU) | 144 | 128 | 128 | 5 | 123 | 16 | 0 |
| Custom delivery quotes | 210 | 89 | 86 | 69 | 17 | 124 | 3 |
| Dispatch orders | 966 | 457 | 457 | 294 | 163 | 509 | 0 |
| Dispatch routes | 24 | 22 | 22 | 12 | 10 | 2 | 0 |
| Dispatch trucks | 8 | 7 | 7 | 0 | 7 | 1 | 0 |
| Dispatch employees | 12 | 11 | 11 | 5 | 6 | 1 | 0 |
| Dispatch notifications | 45 | 60 | 20 | 20 | 0 | 25 | 40 |
| Dispatch stop metrics | 588 | 90 | 90 | 89 | 1 | 498 | 0 |
| Dispatch Shopify updates | 156 | 40 | 40 | 40 | 0 | 116 | 0 |
| App profiles (normalized email) | 12 | 11 | 11 | 9 | 2 | 1 | 0 |
| Dispatch roles (normalized email) | 11 | 1 | 1 | 1 | 0 | 10 | 0 |

No duplicate approved natural keys were detected.

## Timestamp-normalized comparison

Removing only audit timestamps and canonical-only columns produced:

| Data set | Overlap | Substantive conflicts |
|---|---:|---:|
| Product source map | 128 | 28 |
| Custom delivery quotes | 86 | 17 |
| Dispatch orders | 457 | 163 |
| Dispatch routes | 22 | 10 |
| Dispatch trucks | 7 | 7 |
| Dispatch employees | 11 | 0 |
| Dispatch stop metrics | 90 | 1 |
| App profiles | 11 | 2 |

The employee differences are only timestamps and newer canonical columns.

## Privacy-preserving field drift

- Product source map: price 3, variant ID 1, unit label 18, contractor tier
  prices 6 each, pickup vendor 1, and image URL 1.
- Quotes: quote total 1 and creator user ID 16.
- Orders: the material differences are concentrated in route assignment,
  scheduling/status, delivery proof, photos, checklist state, and travel data.
- Routes: code, truck/driver assignment, color, and active state differ.
- Trucks: the continuing V2 model has normalized name/number values and several
  capacity differences.
- Stop metrics: one shared order has learned timing differences.
- App profiles: one name and two permission sets differ.

## Legacy-only readiness

The 40 legacy-only notifications:

- are all already marked read;
- all reference an order that exists in canonical Local-Delivery;
- all reference a route that exists in canonical Local-Delivery;
- have no missing canonical order or route references.

They are safe import candidates after the exact export is available and their
primary keys are rechecked against the final delta.

The three legacy-only quotes do not map to a canonical app profile by normalized
creator email. They require an explicit identity/archive decision before import;
they must not be silently assigned to another user.

## Merge decisions established by this pass

1. Local-Delivery remains authoritative for current orders, routes, trucks,
   employees, stop metrics, product configuration, and user roles.
2. Product-source conflicts are resolved through the current Shopify refresh,
   not by importing stale Quote Live values.
3. The 40 legacy-only notifications are preserved as historical import
   candidates.
4. The three legacy-only quotes and 17 overlapping quote conflicts remain in
   the manual-review queue.
5. The two profile permission/name conflicts remain in the Auth identity-review
   queue.
6. No managed project was modified.

## Remaining gates

- Create and checksum exact encrypted exports for both projects.
- Load the exports into the local reconciliation staging schema.
- Review the three legacy-only quotes, 17 quote conflicts, and two profile
  conflicts using authorized business context.
- Build and approve the Auth identity map.
- Transfer and hash-verify all 469 Local-Delivery Storage objects.
- Re-run this comparison against the final delta immediately before cutover.

