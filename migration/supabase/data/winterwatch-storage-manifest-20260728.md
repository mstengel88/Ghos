# WinterWatch managed Storage export checkpoint

Inventory/export date: 2026-07-28 (America/Chicago)

Managed project: WinterWatch-Pro (`caegybyfdkmgjrygnavg`)

Bucket: private `work-photos`

No object names, paths, contents, credentials, user identifiers, or signed URLs
are recorded in this tracked checkpoint.

## Read-only managed inventory

- Objects: 92
- Declared bytes: 232,094,733
- MIME totals: 89 JPEG and three PNG
- Oldest object timestamp: 2026-01-17
- Newest object timestamp: 2026-03-17
- Bucket restrictions: no explicit file-size or MIME restriction
- Storage policies: six

## Private export result

The guarded exporter downloaded all 92 objects to the ignored private export:

`migration/supabase/exports/storage/winterwatch/initial`

Validation:

- exported objects: 92;
- exported bytes: 232,094,733;
- every object was downloaded privately and SHA-256 hashed;
- the manifest records 92 objects and 232,094,733 bytes;
- the local object-file count is 92; and
- `MANIFEST.SHA256` validates successfully.

Manifest SHA-256:

`382e8555cec5771f9a286e77c469795da84ac5548d9165e96103b7bd275db580`

The ignored export remains sensitive production data. It is not yet counted as
durable recovery media until it is encrypted, copied off the Mac, and restored
byte-for-byte into an isolated WinterWatch lab.
