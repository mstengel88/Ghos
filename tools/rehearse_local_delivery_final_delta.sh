#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
acknowledgement="${GHOS_FINAL_DELTA_ACKNOWLEDGEMENT:-}"
max_skew_seconds="${GHOS_FINAL_DELTA_MAX_SKEW_SECONDS:-10800}"
canonical_archive="${GHOS_FINAL_DELTA_CANONICAL_ARCHIVE:-}"
legacy_archive="${GHOS_FINAL_DELTA_LEGACY_ARCHIVE:-}"
storage_export="${GHOS_FINAL_DELTA_STORAGE_EXPORT:-}"

latest_file() {
  local root="$1"
  local name="$2"
  find "$root" -type f -name "$name" -print 2>/dev/null |
    sort |
    tail -n 1
}

latest_storage_export() {
  local manifest
  manifest="$(
    find \
      "$repo_root/migration/supabase/exports/storage/local-delivery" \
      -mindepth 2 -maxdepth 2 -type f -name manifest.json -print \
      2>/dev/null |
      sort |
      tail -n 1
  )"
  if [[ -n "$manifest" ]]; then
    dirname "$manifest"
  fi
}

if [[ -z "$canonical_archive" ]]; then
  canonical_archive="$(
    latest_file \
      "$repo_root/migration/supabase/exports/local-delivery" \
      local-delivery-database.sql.tar.gz.enc
  )"
fi
if [[ -z "$legacy_archive" ]]; then
  legacy_archive="$(
    latest_file \
      "$repo_root/migration/supabase/exports/greenhills-quote-live" \
      greenhills-quote-live-database.sql.tar.gz.enc
  )"
fi
if [[ -z "$storage_export" ]]; then
  storage_export="$(latest_storage_export)"
fi

if [[ "$acknowledgement" != "UNFROZEN_READ_ONLY_REHEARSAL" &&
  "$acknowledgement" != "WRITES_FROZEN_FINAL_REHEARSAL" ]]; then
  printf '%s\n' \
    'Set GHOS_FINAL_DELTA_ACKNOWLEDGEMENT to one of:' \
    '  UNFROZEN_READ_ONLY_REHEARSAL' \
    '  WRITES_FROZEN_FINAL_REHEARSAL' >&2
  exit 2
fi
if [[ ! "$max_skew_seconds" =~ ^[0-9]+$ ]] ||
  ((max_skew_seconds < 1)); then
  printf 'GHOS_FINAL_DELTA_MAX_SKEW_SECONDS must be a positive integer.\n' >&2
  exit 2
fi
if [[ "$acknowledgement" == "WRITES_FROZEN_FINAL_REHEARSAL" ]] &&
  ((max_skew_seconds > 3600)); then
  printf '%s\n' \
    'A writes-frozen final rehearsal requires input skew of one hour or less.' \
    'Set GHOS_FINAL_DELTA_MAX_SKEW_SECONDS to 3600 or less.' >&2
  exit 2
fi

for command_name in docker git python3 shasum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done

for archive in "$canonical_archive" "$legacy_archive"; do
  if [[ ! -f "$archive" || ! -s "$archive" ]]; then
    printf 'Required encrypted database archive is missing: %s\n' \
      "$archive" >&2
    exit 1
  fi
  if [[ ! -f "$archive.sha256" ]]; then
    printf 'Database archive checksum is missing: %s.sha256\n' \
      "$archive" >&2
    exit 1
  fi
  if ! git -C "$repo_root" check-ignore -q "$archive"; then
    printf 'Database archive is not protected by Git ignore rules: %s\n' \
      "$archive" >&2
    exit 1
  fi
  (
    cd "$(dirname "$archive")"
    shasum -a 256 -c "$(basename "$archive").sha256"
  )
done

for required_file in \
  "$storage_export/manifest.json" \
  "$storage_export/MANIFEST.SHA256" \
  "$storage_export/local-restore-report.json"; do
  if [[ ! -f "$required_file" ]]; then
    printf 'Verified Storage evidence is missing: %s\n' \
      "$required_file" >&2
    exit 1
  fi
done
if [[ ! -d "$storage_export/objects" ]]; then
  printf 'Storage object directory is missing: %s/objects\n' \
    "$storage_export" >&2
  exit 1
fi
if ! git -C "$repo_root" check-ignore -q "$storage_export"; then
  printf 'Storage export is not protected by Git ignore rules: %s\n' \
    "$storage_export" >&2
  exit 1
fi
(
  cd "$storage_export"
  shasum -a 256 -c MANIFEST.SHA256
)

python3 - \
  "$storage_export" \
  "$canonical_archive" \
  "$legacy_archive" \
  "$max_skew_seconds" <<'PY'
import datetime
import hashlib
import json
import pathlib
import re
import sys

storage_root = pathlib.Path(sys.argv[1]).resolve()
canonical_archive = pathlib.Path(sys.argv[2]).resolve()
legacy_archive = pathlib.Path(sys.argv[3]).resolve()
max_skew_seconds = int(sys.argv[4])


def sha256_path(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()


manifest = json.loads(
    (storage_root / "manifest.json").read_text(encoding="utf-8")
)
report = json.loads(
    (storage_root / "local-restore-report.json").read_text(encoding="utf-8")
)
objects = manifest.get("objects")
if manifest.get("bucket") != "dispatch-photos" or not isinstance(objects, list):
    raise SystemExit("Storage manifest is not for dispatch-photos.")

expected_count = int(manifest.get("object_count", -1))
expected_bytes = int(manifest.get("total_bytes", -1))
if expected_count != len(objects):
    raise SystemExit("Storage manifest object count is inconsistent.")

actual_bytes = 0
for entry in objects:
    relative = pathlib.PurePosixPath(str(entry.get("name") or ""))
    if (
        relative.is_absolute()
        or not relative.parts
        or any(part in {"", ".", ".."} for part in relative.parts)
    ):
        raise SystemExit("Storage manifest contains an unsafe object path.")
    source = storage_root / "objects" / pathlib.Path(*relative.parts)
    if not source.is_file():
        raise SystemExit("A Storage object is missing.")
    size = source.stat().st_size
    if size != int(entry.get("size", -1)):
        raise SystemExit("A Storage object size does not match its manifest.")
    if sha256_path(source) != str(entry.get("sha256") or ""):
        raise SystemExit("A Storage object failed SHA-256 verification.")
    actual_bytes += size

if actual_bytes != expected_bytes:
    raise SystemExit("Storage byte total does not match its manifest.")
if (
    int(report.get("object_count", -1)) != expected_count
    or int(report.get("total_bytes", -1)) != expected_bytes
    or int(report.get("hash_mismatches", -1)) != 0
):
    raise SystemExit("Storage restore report does not match the export.")


def archive_timestamp(path: pathlib.Path) -> datetime.datetime:
    match = re.fullmatch(r"(\d{8}T\d{6}Z)", path.parent.name)
    if match is None:
        raise SystemExit(
            f"Database archive directory lacks a UTC extraction timestamp: {path}"
        )
    return datetime.datetime.strptime(
        match.group(1), "%Y%m%dT%H%M%SZ"
    ).replace(tzinfo=datetime.timezone.utc)


generated_value = (
    manifest.get("generated_at")
    or manifest.get("created_at")
    or manifest.get("exported_at")
)
if not isinstance(generated_value, str):
    raise SystemExit("Storage manifest lacks its generation timestamp.")
storage_timestamp = datetime.datetime.fromisoformat(
    generated_value.replace("Z", "+00:00")
)
timestamps = [
    archive_timestamp(canonical_archive),
    archive_timestamp(legacy_archive),
    storage_timestamp,
]
skew = (max(timestamps) - min(timestamps)).total_seconds()
if skew > max_skew_seconds:
    raise SystemExit(
        f"Cutover inputs span {int(skew)} seconds; allowed maximum is "
        f"{max_skew_seconds}."
    )

print(
    f"Verified {expected_count} Storage objects ({expected_bytes} bytes) "
    f"and cutover-input skew of {int(skew)} seconds."
)
PY

GHOS_RECONCILE_CANONICAL_ARCHIVE="$canonical_archive" \
GHOS_RECONCILE_LEGACY_ARCHIVE="$legacy_archive" \
  "$repo_root/tools/rehearse_local_delivery_quote_merge.sh"

printf '%s\n' \
  '' \
  "Final-input rehearsal mode: $acknowledgement" \
  'Database archives, Storage bytes, owner disposition, and the' \
  'notification-only merge passed in disposable local infrastructure.' \
  'No production database or Storage object was changed.'
if [[ "$acknowledgement" == "UNFROZEN_READ_ONLY_REHEARSAL" ]]; then
  printf '%s\n' \
    'This is not cutover authorization: source writes were not frozen and a' \
    'fresh final export is still required during the maintenance window.'
fi
