# Local-Delivery final-delta readiness

Date: 2026-07-30 UTC

Status: read-only live drift measured; final write freeze and fresh exports
remain required

No production row or Storage object was changed during this check.

## Protected checkpoint inputs

| Input | Extraction time | Verified checkpoint |
|---|---|---|
| Local-Delivery database/Auth/Storage metadata | 2026-07-30 00:50:31 UTC | Encrypted archive and checksum |
| Local-Delivery `dispatch-photos` bytes | 2026-07-30 01:27:19 UTC | 475 objects, 643,127,719 bytes, zero restore mismatches |
| GreenHills Quote Live database/Auth/Storage metadata | 2026-07-30 03:15:08 UTC | Encrypted archive and checksum |

The selected merge policy remains:

- Local-Delivery owns all continuing dispatch and quote state;
- import exactly the 40 verified Quote Live-only read notifications;
- import no Quote Live quote;
- retain the three legacy-only quotes only in the encrypted rollback archive;
- keep the canonical Local-Delivery row for the one duplicate quote conflict;
- refresh product data from Shopify instead of copying legacy product-map
  conflicts.

## Read-only live drift check

The live aggregate check at 2026-07-30 12:13:59 UTC found:

| Relation | Signed checkpoint | Live count | Drift |
|---|---:|---:|---:|
| Local-Delivery Auth users | 13 | 13 | 0 |
| Local-Delivery quotes | 212 | 212 | 0 |
| Local-Delivery notifications | 45 | 45 | 0 |
| Local-Delivery orders | 970 | 971 | +1 |
| Local-Delivery routes | 24 | 24 | 0 |
| Local-Delivery stop metrics | 592 | 592 | 0 |
| Local-Delivery product map | 144 | 144 | 0 |
| Local-Delivery Storage objects | 475 | 475 | 0 |
| Quote Live Auth users | 11 | 11 | 0 |
| Quote Live quotes | 89 | 89 | 0 |
| Quote Live notifications | 60 | 60 | 0 |
| Quote Live orders | 457 | 457 | 0 |
| Quote Live routes | 22 | 22 | 0 |
| Quote Live stop metrics | 90 | 90 | 0 |
| Quote Live product map | 128 | 128 | 0 |
| Quote Live Storage objects | 0 | 0 | 0 |

Local-Delivery also reported one order created and ten orders updated after the
signed database extraction. No order was marked delivered after the extraction
time. Counts alone do not prove that unchanged-count tables have unchanged
content, so a fresh signed export remains mandatory.

## Rehearsal command

The final-input rehearsal accepts explicit signed database archives and a
verified Storage byte export:

```bash
GHOS_FINAL_DELTA_ACKNOWLEDGEMENT=UNFROZEN_READ_ONLY_REHEARSAL \
  ./tools/rehearse_local_delivery_final_delta.sh
```

It verifies:

- both encrypted database archive checksums;
- the Storage manifest, all object sizes, and all object SHA-256 values;
- the prior isolated Storage restore report;
- extraction-time skew across the three inputs;
- the exact reconciliation baseline and owner quote disposition;
- a notification-only merge into a disposable Local-Delivery clone; and
- cleanup of every disposable database and plaintext work directory.

An unfrozen run validates the tooling only. It is not cutover authorization.

## Maintenance-window boundary

For the actual final rehearsal:

1. Pause writes to Dispatch V2, the GHOS quote tool, and Quote Live.
2. Confirm no background importer, webhook, or scheduled job can create or
   update dispatch or quote records.
3. Capture fresh encrypted Local-Delivery and Quote Live database archives.
4. Capture a fresh `dispatch-photos` export and complete its isolated restore
   verification.
5. Run the tool with
   `GHOS_FINAL_DELTA_ACKNOWLEDGEMENT=WRITES_FROZEN_FINAL_REHEARSAL` and an
   extraction skew of 3,600 seconds or less.
6. Review exact counts, checksums, Auth/RLS acceptance, Storage hashes, and
   external integrations before authorizing cutover.
7. Keep both managed Supabase projects intact throughout the observation and
   rollback window.
