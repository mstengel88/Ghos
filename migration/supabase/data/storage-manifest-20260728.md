# Local-Delivery photo migration manifest

Run date: 2026-07-28

Source: managed Local-Delivery project

Status: initial object-byte export and isolated localhost restore complete and
verified. Encryption, off-Mac backup, database-linked metadata rehearsal, and
final-delta export remain pending.

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

## Initial byte export

The private ignored export completed on 2026-07-28:

- 470 files;
- 640,756,931 actual bytes;
- zero missing files;
- zero SHA-256 mismatches;
- zero retained partial files.

The SHA-256 checksum of the private byte manifest is:

```text
60e1db6162c1894a00a4729132ebb84004b77995d5c88dcf5af2d8fa7bd7de21
```

The private manifest, object names, metadata, and image bytes remain under the
ignored migration export directory and are not committed. This is a verified
local copy, not yet a complete backup: it still needs encryption and an
off-Mac copy.

## Isolated restore acceptance

The private export was restored through the Storage API into the localhost
Supabase compatibility lab:

- 470 objects uploaded;
- 640,756,931 bytes represented;
- every restored object downloaded back from the lab;
- zero SHA-256 mismatches.

A second run reused and reverified all 470 matching objects without overwriting
them. The current private restore report SHA-256 is:

```text
61cfdb997c8f9b5b5c70859a23a19bc1ce0e32165d4f32d1e678c976cadfaf79
```

The restore report and all object-level evidence remain ignored. This validates
object-byte portability. Exact `storage.objects` IDs, timestamps, owner fields,
and database relationships still depend on the database export and must be
checked during the full rehearsal.

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
2. Preserve the verified export of all 470 Storage objects and their metadata.
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

## 2026-07-29 refreshed byte snapshot

This document's earlier sections preserve the 2026-07-28 point-in-time
inventory. Because Local-Delivery remained writable, the private export was
refreshed on 2026-07-29:

- 475 objects;
- 643,127,719 bytes;
- private manifest SHA-256
  `76370e8e14d248ba915eabac0b4904ec40a155f011a58d44ef4a3440dbbe428e`;
- five new local uploads and 470 matching objects reused; and
- zero SHA-256 mismatches after downloading all 475 restored objects.

The exporter rejected and retried one transient short HTTP response before
creating the manifest. The verified export was then encrypted with
AES-256-CBC/PBKDF2, independently decrypted and opened, and protected by a
separate archive checksum. All object-level evidence and archive files remain
in Git-ignored private directories. A final delta is still required after
production writes are paused.
