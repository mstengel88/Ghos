# Sanitized Auth identity reconciliation

Run date: 2026-07-28

Canonical project: Local-Delivery / Dispatch V2 Sandbox

Legacy comparison project: GreenHills Quote Live

Status: read-only identity classification complete; exact Auth export and
identity rewrite are not started.

## Privacy-preserving method

The comparison ran read-only SQL through each project-scoped Supabase MCP
connection. Normalized emails, Auth user IDs, encrypted password values, and
user metadata were combined with context and SHA-256 hashed inside each managed
database. Only aggregate overlap, match, and reference counts were retained.

No email address, user ID, password hash, metadata value, token, or secret was
exported or committed.

## Results

| Classification | Count |
|---|---:|
| Local-Delivery Auth users | 13 |
| Quote Live Auth users | 11 |
| Confirmed Local-Delivery email users | 13 |
| Confirmed Quote Live email users | 11 |
| Overlapping normalized emails | 11 |
| Local-Delivery-only users | 2 |
| Quote Live-only users | 0 |
| Overlap with matching Auth UUID | 9 |
| Overlap with different Auth UUID | 2 |
| Overlap with matching encrypted-password fingerprint | 8 |
| Overlap with different encrypted-password fingerprint | 3 |
| Overlap with matching user-metadata fingerprint | 8 |
| Overlap with different user-metadata fingerprint | 3 |

All users in both projects currently use the email provider.

## Divergent UUID impact

The two overlapping people with different Auth UUIDs currently own:

| Reference | Quote Live legacy UUID | Local-Delivery canonical UUID |
|---|---:|---:|
| Custom quote rows | 16 | 50 |
| App profile rows | 2 | 2 |
| Dispatch role rows | 0 | 2 |

This exactly explains the 16 overlapping quote conflicts previously classified
as `created_by_user_id` drift.

## Migration decisions

1. Local-Delivery Auth identities are canonical.
2. Quote Live Auth rows must not overwrite Local-Delivery Auth rows.
3. During quote import, the 16 legacy quote creator UUIDs are rewritten through
   the approved normalized-email identity map to the canonical Local-Delivery
   UUIDs.
4. Local-Delivery profiles and dispatch roles remain authoritative.
5. The three password-fingerprint differences require an explicit credential
   decision. Default behavior is to retain the Local-Delivery credential and
   offer a password-reset flow after cutover; never copy one managed password
   hash over another without an exact Auth restore rehearsal.
6. The three user-metadata differences remain in the manual identity-review
   queue.
7. The three legacy-only quotes whose creator does not map to a canonical
   profile remain separate from this resolved 16-row UUID rewrite.

## Remaining gates

- Acquire exact encrypted Auth/database exports.
- Generate the private email-to-canonical-UUID map from those exports.
- Rehearse the 16 quote reference rewrites in isolated staging.
- Decide how the three credential and three metadata differences are handled.
- Verify login, recovery, profile, role, and quote ownership after restore.
- Re-run the sanitized comparison against the final production delta.
