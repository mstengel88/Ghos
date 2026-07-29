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

## Recovery rehearsal

The ignored export was copied to the GHOS VM over the encrypted Tailscale/SSH
path on 2026-07-28. The VM copy independently verified:

- 92 object files;
- 232,094,733 object bytes;
- the approved manifest SHA-256; and
- zero per-object SHA-256 mismatches.

The same export was restored into the isolated localhost Supabase lab and every
restored object was downloaded again for SHA-256 comparison:

- uploaded: 92;
- reused: 0;
- verified: 92;
- hash mismatches: 0; and
- verified bytes: 232,094,733.

The off-Mac VM copy remains sensitive production data and is excluded from Git.
The root-only GHOS backup service included it in encrypted Backblaze B2
snapshot `39e3ff2d` on 2026-07-28:

- service result: success;
- process exit status: 0;
- snapshot size: approximately 320.2 MiB; and
- backup run: `20260729T020300Z`.

This completes the private Storage export, off-Mac encrypted retention, and
isolated byte-for-byte restore gates. The full WinterWatch database/Auth restore
remains separate work.
