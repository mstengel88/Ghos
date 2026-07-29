#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

if [[ "$EUID" -ne 0 ]]; then
  printf 'Run this command with sudo on the GHOS VM.\n' >&2
  exit 1
fi

export_dir=/opt/ghos/migration/supabase/exports/storage/winterwatch/initial
source_config=/etc/ghos-backup/source-paths.conf
expected_manifest_hash=382e8555cec5771f9a286e77c469795da84ac5548d9165e96103b7bd275db580

[[ -d "$export_dir/objects" ]] || {
  printf 'WinterWatch Storage export is missing: %s\n' "$export_dir" >&2
  exit 1
}
[[ -f "$source_config" ]] || {
  printf 'GHOS backup source configuration is missing: %s\n' "$source_config" >&2
  exit 1
}

python3 - "$export_dir" "$expected_manifest_hash" <<'PY'
import hashlib
import json
import sys
from pathlib import Path, PurePosixPath

root = Path(sys.argv[1])
expected_manifest_hash = sys.argv[2]
manifest_path = root / "manifest.json"
checksum_path = root / "MANIFEST.SHA256"

def sha256_path(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(1024 * 1024):
            digest.update(chunk)
    return digest.hexdigest()

if not manifest_path.is_file() or not checksum_path.is_file():
    raise SystemExit("WinterWatch manifest or checksum is missing.")
if sha256_path(manifest_path) != expected_manifest_hash:
    raise SystemExit("WinterWatch manifest hash does not match the approved checkpoint.")
if checksum_path.read_text(encoding="utf-8").split()[0] != expected_manifest_hash:
    raise SystemExit("WinterWatch manifest checksum file does not match the approved checkpoint.")

manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
entries = manifest.get("objects")
if manifest.get("bucket") != "work-photos" or not isinstance(entries, list):
    raise SystemExit("WinterWatch Storage manifest is invalid.")
if len(entries) != 92 or sum(int(entry["size"]) for entry in entries) != 232094733:
    raise SystemExit("WinterWatch Storage count or byte total does not match the approved checkpoint.")

for entry in entries:
    relative = PurePosixPath(str(entry.get("name") or ""))
    if relative.is_absolute() or not relative.parts or any(
        part in {"", ".", ".."} for part in relative.parts
    ):
        raise SystemExit("WinterWatch manifest contains an unsafe object path.")
    source = root / "objects" / Path(*relative.parts)
    if (
        not source.is_file()
        or source.stat().st_size != int(entry["size"])
        or sha256_path(source) != str(entry["sha256"])
    ):
        raise SystemExit("A WinterWatch Storage object failed verification.")

print("Verified 92 WinterWatch Storage objects (232094733 bytes).")
PY

if ! grep -Fqx -- "$export_dir" "$source_config"; then
  backup_copy="${source_config}.before-winterwatch-$(date -u +%Y%m%dT%H%M%SZ)"
  cp --preserve=mode,ownership,timestamps "$source_config" "$backup_copy"
  printf '%s\n' "$export_dir" >>"$source_config"
  chmod 0600 "$source_config"
  printf 'Registered WinterWatch Storage with the GHOS backup sources.\n'
else
  printf 'WinterWatch Storage is already registered with the GHOS backup sources.\n'
fi

systemctl start ghos-backup.service
systemctl --no-pager --full status ghos-backup.service

