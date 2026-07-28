# Local-Delivery photo migration manifest

Run date: 2026-07-28

Source: managed Local-Delivery project

Status: read-only metadata and reference inventory complete; object-byte export
not started.

## Supabase Storage

The `dispatch-photos` bucket currently contains:

- 470 objects;
- 640,756,931 bytes;
- 468 JPEG objects totaling 638,181,657 bytes;
- two PNG objects totaling 2,575,274 bytes;
- objects created from 2026-05-28 through 2026-07-28;
- eight repeated ETag groups requiring duplicate review.

The privacy-preserving metadata manifest digest is:

```text
SHA-256 0c243439d91cee558f801618b8d868a012d6cde0ef23ca5655636edf13b91ba2
```

The digest covers the sorted object name, declared size, and ETag for every
object. Object names and ETags are not retained in Git.

GreenHills Quote Live still has zero objects in its `dispatch-photos` bucket.

## Database photo references

`dispatch_orders.photo_urls` is a text field, not a JSON array. The current
reference inventory found:

- 626 orders with non-empty photo text;
- 452 orders containing a Supabase Storage URL;
- 452 distinct Storage objects referenced by those orders;
- 18 Storage objects not referenced by current order photo text;
- 174 orders with photo text that did not resolve to a Storage object.

The 174 non-Storage records consist of:

- 170 orders containing embedded JPEG data URLs;
- four records containing neither a data-image URL nor an HTTP URL.

Embedded JPEG text occupies 151,395,742 characters, and the largest single
field is 1,176,235 characters. These values are database payloads and will not
be transferred by copying the Storage bucket alone.

## Migration consequences

1. Export `dispatch_orders.photo_urls` with the database, including the large
   embedded JPEG payloads.
2. Export all 470 Storage objects and their metadata independently.
3. Preserve the 18 currently unreferenced Storage objects until retention and
   duplicate decisions are approved.
4. Quarantine and inspect the four unclassified photo-text records during the
   encrypted local rehearsal.
5. Compare object bytes by SHA-256 after restore; ETags alone are not sufficient
   integrity proof.
6. Recompute this manifest immediately before final cutover because Dispatch V2
   remains writable.

No image bytes, paths, customer data, or secret values were exported during
this inventory.

