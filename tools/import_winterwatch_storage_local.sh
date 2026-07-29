#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
storage_container="${SUPABASE_STORAGE_CONTAINER:-supabase-storage}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
database_name="${WINTERWATCH_REHEARSAL_DATABASE:-}"
export_root="$repo_root/migration/supabase/exports/storage/winterwatch/initial"
manifest="$export_root/manifest.json"
expected_bucket="work-photos"
storage_root="/var/lib/storage/stub/stub"
mapping_file="$(mktemp "${TMPDIR:-/tmp}/winterwatch-storage-map.XXXXXX")"

cleanup() {
  rm -f "$mapping_file"
}
trap cleanup EXIT

for command_name in docker jq mktemp python3 shasum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ ! -s "$manifest" || ! -d "$export_root/objects" ]]; then
  printf 'WinterWatch private Storage export is incomplete: %s\n' \
    "$export_root" >&2
  exit 1
fi
for container_name in "$db_container" "$storage_container"; do
  if ! docker inspect "$container_name" >/dev/null 2>&1; then
    printf 'Required local Supabase container is unavailable: %s\n' \
      "$container_name" >&2
    exit 1
  fi
done
if ! docker exec "$storage_container" command -v setfattr >/dev/null 2>&1; then
  printf '%s\n' \
    'Installing the temporary Alpine attr utility in the local Storage container...'
  docker exec -u 0 "$storage_container" \
    apk add --no-cache attr >/dev/null
fi

if [[ -z "$database_name" ]]; then
  database_name="$(
    docker exec "$db_container" \
      psql -v ON_ERROR_STOP=1 -U "$database_admin" -d postgres -Atqc \
        "select datname
         from pg_database
         where datname ~ '^winterwatch_rehearsal_[0-9]{8}_[0-9]{6}$'
         order by datname desc
         limit 1"
  )"
fi
if [[ ! "$database_name" =~ ^winterwatch_rehearsal_[0-9]{8}_[0-9]{6}$ ]]; then
  printf 'Unsafe or missing WinterWatch rehearsal database: %s\n' \
    "$database_name" >&2
  exit 1
fi

docker exec "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$database_name" \
    -AtF $'\t' \
    -c "select id::text, version, bucket_id, name
        from storage.objects
        where bucket_id = '$expected_bucket'
        order by name" >"$mapping_file"

python3 - "$manifest" "$mapping_file" "$export_root/objects" <<'PY'
import hashlib
import json
import sys
from pathlib import Path, PurePosixPath

manifest_path = Path(sys.argv[1])
mapping_path = Path(sys.argv[2])
objects_root = Path(sys.argv[3])
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

if manifest.get("bucket") != "work-photos":
    raise SystemExit("Unexpected WinterWatch Storage bucket.")
entries = manifest.get("objects")
if not isinstance(entries, list) or len(entries) != 92:
    raise SystemExit("WinterWatch Storage manifest must contain exactly 92 objects.")

expected = {str(entry["name"]): entry for entry in entries}
mapping = {}
for line in mapping_path.read_text(encoding="utf-8").splitlines():
    object_id, object_version, bucket_id, name = line.split("\t", 3)
    if bucket_id != "work-photos" or name in mapping:
        raise SystemExit("WinterWatch Storage database mapping is invalid.")
    if not object_id or not object_version:
        raise SystemExit("WinterWatch Storage object version is missing.")
    mapping[name] = (object_id, object_version)

if set(mapping) != set(expected):
    missing = sorted(set(expected) - set(mapping))
    extra = sorted(set(mapping) - set(expected))
    raise SystemExit(
        f"Storage metadata does not match the private export "
        f"(missing={len(missing)}, extra={len(extra)})."
    )

for name, entry in expected.items():
    relative = PurePosixPath(name)
    if relative.is_absolute() or any(
        part in {"", ".", ".."} for part in relative.parts
    ):
        raise SystemExit("Unsafe object path in WinterWatch manifest.")
    source = objects_root.joinpath(*relative.parts)
    if not source.is_file() or source.stat().st_size != int(entry["size"]):
        raise SystemExit(f"Missing or invalid private object: {name}")
    digest = hashlib.sha256(source.read_bytes()).hexdigest()
    if digest != str(entry["sha256"]):
        raise SystemExit(f"Hash mismatch in private object: {name}")

with mapping_path.open("w", encoding="utf-8") as output:
    for name in sorted(mapping):
        object_id, object_version = mapping[name]
        metadata = expected[name].get("metadata") or {}
        content_type = str(metadata.get("mimetype") or "application/octet-stream")
        cache_control = str(metadata.get("cacheControl") or "no-cache")
        if any(character in content_type + cache_control for character in "\t\r\n"):
            raise SystemExit("Unsafe Storage object metadata.")
        output.write(
            f"{object_id}\t{object_version}\twork-photos\t{name}\t"
            f"{content_type}\t{cache_control}\n"
        )

print("Verified the 92-object database-to-file mapping.")
PY

imported=0
reused=0
cleaned=0
while IFS=$'\t' read -r object_id object_version bucket_id object_name \
  content_type cache_control; do
  source_path="$export_root/objects/$object_name"
  destination_dir="$storage_root/$bucket_id/$object_name"
  destination_path="$destination_dir/$object_version"
  expected_hash="$(shasum -a 256 "$source_path" | awk '{print $1}')"

  docker exec "$storage_container" mkdir -p "$destination_dir"
  if docker exec "$storage_container" test -f "$destination_path"; then
    actual_hash="$(
      docker exec "$storage_container" sha256sum "$destination_path" |
        awk '{print $1}'
    )"
    if [[ "$actual_hash" != "$expected_hash" ]]; then
      printf 'Refusing to overwrite a conflicting local Storage object.\n' >&2
      exit 1
    fi
    reused=$((reused + 1))
  else
    docker cp "$source_path" "$storage_container:$destination_path" >/dev/null
    actual_hash="$(
      docker exec "$storage_container" sha256sum "$destination_path" |
        awk '{print $1}'
    )"
    if [[ "$actual_hash" != "$expected_hash" ]]; then
      printf 'Imported Storage object failed its hash check.\n' >&2
      exit 1
    fi
    imported=$((imported + 1))
  fi

  docker exec "$storage_container" \
    setfattr -n user.supabase.content-type -v "$content_type" \
    "$destination_path"
  docker exec "$storage_container" \
    setfattr -n user.supabase.cache-control -v "$cache_control" \
    "$destination_path"

  legacy_path="$destination_dir/$object_id"
  if [[ "$legacy_path" != "$destination_path" ]] &&
    docker exec "$storage_container" test -f "$legacy_path"; then
    legacy_hash="$(
      docker exec "$storage_container" sha256sum "$legacy_path" |
        awk '{print $1}'
    )"
    if [[ "$legacy_hash" != "$expected_hash" ]]; then
      printf 'Refusing to remove a conflicting legacy local object.\n' >&2
      exit 1
    fi
    docker exec "$storage_container" unlink "$legacy_path"
    cleaned=$((cleaned + 1))
  fi
done <"$mapping_file"

printf \
  'WinterWatch private Storage import complete: %d imported, %d reused, %d stale copies cleaned.\n' \
  "$imported" "$reused" "$cleaned"
