#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
export_root="${1:-}"
keychain_account="${GHOS_STORAGE_ARCHIVE_KEYCHAIN_ACCOUNT:-local-delivery}"
keychain_service="GHOS Migration Export Encryption"

if [[ -z "$export_root" ]]; then
  printf 'Usage: %s <verified-storage-export-directory>\n' "$0" >&2
  exit 2
fi

export_root="$(
  cd "$export_root"
  pwd
)"
manifest="$export_root/manifest.json"
manifest_checksum="$export_root/MANIFEST.SHA256"
restore_report="$export_root/local-restore-report.json"

for command_name in openssl python3 security shasum tar; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command is unavailable: %s\n' "$command_name" >&2
    exit 1
  fi
done

for required_file in \
  "$manifest" \
  "$manifest_checksum" \
  "$restore_report"; do
  if [[ ! -f "$required_file" ]]; then
    printf 'Required verified export evidence is missing.\n' >&2
    exit 1
  fi
done

if ! git -C "$repo_root" check-ignore -q "$export_root"; then
  printf 'Storage export is not ignored by Git; stopping.\n' >&2
  exit 1
fi

(
  cd "$export_root"
  shasum -a 256 -c MANIFEST.SHA256
)

python3 - "$export_root" <<'PY'
import hashlib
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
objects = manifest.get("objects")
if not isinstance(objects, list):
    raise SystemExit("The private manifest object list is invalid.")

expected_count = int(manifest.get("object_count", -1))
expected_bytes = int(manifest.get("total_bytes", -1))
if expected_count != len(objects):
    raise SystemExit("The private manifest object count is inconsistent.")

actual_bytes = 0
for entry in objects:
    relative = pathlib.PurePosixPath(str(entry.get("name", "")))
    if (
        relative.is_absolute()
        or not relative.parts
        or any(part in {"", ".", ".."} for part in relative.parts)
    ):
        raise SystemExit("The private manifest contains an unsafe object path.")
    path = root / "objects" / relative
    if not path.is_file():
        raise SystemExit("A private manifest object is missing.")
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    size = path.stat().st_size
    if size != int(entry.get("size", -1)):
        raise SystemExit("A private manifest object has an unexpected size.")
    if digest.hexdigest() != entry.get("sha256"):
        raise SystemExit("A private manifest object failed SHA-256 verification.")
    actual_bytes += size

if actual_bytes != expected_bytes:
    raise SystemExit("The private manifest byte total is inconsistent.")

report = json.loads(
    (root / "local-restore-report.json").read_text(encoding="utf-8")
)
if (
    int(report.get("object_count", -1)) != expected_count
    or int(report.get("total_bytes", -1)) != expected_bytes
    or int(report.get("hash_mismatches", -1)) != 0
):
    raise SystemExit("The local restore report does not match the export.")

print(
    f"Verified {expected_count} objects and {expected_bytes} bytes "
    "before encryption."
)
PY

encryption_password="$(
  security find-generic-password \
    -s "$keychain_service" \
    -a "$keychain_account" \
    -w
)"
if [[ ${#encryption_password} -lt 20 ]]; then
  printf 'The migration archive encryption password is unavailable.\n' >&2
  exit 1
fi

archive_parent="$(dirname "$export_root")/archives"
archive_timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
archive_name="local-delivery-storage-$archive_timestamp.tar.gz"
encrypted_archive="$archive_parent/$archive_name.enc"
encrypted_checksum="$encrypted_archive.sha256"
temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-storage-package.XXXXXX")"
plain_archive="$temporary_root/$archive_name"
verified_archive="$temporary_root/$archive_name.verified"

cleanup() {
  unset encryption_password GHOS_EXPORT_PASSWORD
  find "$temporary_root" -type f -delete 2>/dev/null || true
  rmdir "$temporary_root" 2>/dev/null || true
}
trap cleanup EXIT

mkdir -p "$archive_parent"
tar -C "$export_root" -czf "$plain_archive" \
  MANIFEST.SHA256 \
  manifest.json \
  source-listing.json \
  local-restore-report.json \
  objects

GHOS_EXPORT_PASSWORD="$encryption_password" \
  openssl enc \
    -aes-256-cbc \
    -salt \
    -pbkdf2 \
    -iter 250000 \
    -md sha256 \
    -pass env:GHOS_EXPORT_PASSWORD \
    -in "$plain_archive" \
    -out "$encrypted_archive"

GHOS_EXPORT_PASSWORD="$encryption_password" \
  openssl enc \
    -d \
    -aes-256-cbc \
    -pbkdf2 \
    -iter 250000 \
    -md sha256 \
    -pass env:GHOS_EXPORT_PASSWORD \
    -in "$encrypted_archive" \
    -out "$verified_archive"

tar -tzf "$verified_archive" >/dev/null
(
  cd "$archive_parent"
  shasum -a 256 "$(basename "$encrypted_archive")" \
    > "$(basename "$encrypted_checksum")"
  shasum -a 256 -c "$(basename "$encrypted_checksum")"
)
chmod 600 "$encrypted_archive" "$encrypted_checksum"

if ! git -C "$repo_root" check-ignore -q "$encrypted_archive"; then
  printf 'Encrypted archive is not ignored by Git; stopping.\n' >&2
  exit 1
fi

printf 'Encrypted Storage archive verified: %s\n' "$encrypted_archive"
printf 'Archive checksum: %s\n' "$encrypted_checksum"
