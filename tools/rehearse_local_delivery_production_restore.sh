#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
database_name="${LOCAL_DELIVERY_REHEARSAL_DATABASE:-local_delivery_restore_$(date -u +%Y%m%d_%H%M%S)}"
archive_path="${LOCAL_DELIVERY_DATABASE_ARCHIVE:-}"
retain_database="${LOCAL_DELIVERY_RETAIN_REHEARSAL_DATABASE:-0}"
keychain_service="GHOS Migration Export Encryption"
keychain_account="local-delivery"
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-local-delivery-restore.XXXXXX")"
export_root="$work_root/export"
plain_archive="$work_root/local-delivery-database.sql.tar.gz"
rehearsal_schema="$work_root/schema.rehearsal.sql"
rehearsal_data="$work_root/data.rehearsal.sql"
reconciled_counts="$work_root/expected-counts.reconciled.tsv"
encryption_password=""
database_created=0
services_stopped=0
app_containers=()

database_exists() {
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 -Atqc \
      "select 1 from pg_database where datname = '$1'" |
    grep -qx 1
}

start_application_services() {
  if ((${#app_containers[@]})); then
    docker start "${app_containers[@]}" >/dev/null
    sleep 2
    for container_name in "${app_containers[@]}"; do
      if [[ "$(docker inspect -f '{{.State.Running}}' "$container_name")" \
        != "true" ]]; then
        printf 'Failed to restart local Supabase service: %s\n' \
          "$container_name" >&2
        return 1
      fi
    done
  fi
  services_stopped=0
}

stop_application_services() {
  if ((${#app_containers[@]})); then
    docker stop -t 30 "${app_containers[@]}" >/dev/null
  fi
  services_stopped=1
}

drop_rehearsal_database() {
  if [[ "$database_created" == 1 ]] && database_exists "$database_name"; then
    docker exec "$db_container" \
      dropdb -U "$database_admin" --if-exists --force "$database_name" \
      >/dev/null
    database_created=0
  fi
}

cleanup() {
  set +e
  if [[ "$services_stopped" == 1 ]]; then
    start_application_services
  fi
  if [[ "$retain_database" != "1" ]]; then
    drop_rehearsal_database
  fi
  unset encryption_password
  rm -rf "$work_root"
}
trap cleanup EXIT

for command_name in docker openssl python3 security shasum tar; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi
if [[ ! "$database_name" =~ ^[a-z_][a-z0-9_]*$ ]]; then
  printf 'Unsafe database name: %s\n' "$database_name" >&2
  exit 1
fi
if [[ "$retain_database" != "0" && "$retain_database" != "1" ]]; then
  printf 'LOCAL_DELIVERY_RETAIN_REHEARSAL_DATABASE must be 0 or 1.\n' >&2
  exit 1
fi
if [[ "$(
  docker inspect \
    --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' \
    "$db_container"
)" != "running|healthy" ]]; then
  printf '%s\n' 'The local Supabase database is not healthy.' >&2
  exit 1
fi

if [[ -z "$archive_path" ]]; then
  archive_path="$(
    find "$repo_root/migration/supabase/exports/local-delivery" \
      -type f -name 'local-delivery-database.sql.tar.gz.enc' \
      -print 2>/dev/null |
      sort |
      tail -n 1
  )"
fi
if [[ -z "$archive_path" || ! -s "$archive_path" ]]; then
  printf '%s\n' \
    'No encrypted Local Delivery database archive was found.' \
    'Set LOCAL_DELIVERY_DATABASE_ARCHIVE to the archive path.' >&2
  exit 1
fi
if [[ -f "$archive_path.sha256" ]]; then
  (
    cd "$(dirname "$archive_path")"
    shasum -a 256 -c "$(basename "$archive_path").sha256"
  )
fi

encryption_password="$(
  security find-generic-password \
    -s "$keychain_service" \
    -a "$keychain_account" \
    -w
)"
GHOS_EXPORT_PASSWORD="$encryption_password" \
  openssl enc \
    -d \
    -aes-256-cbc \
    -pbkdf2 \
    -iter 250000 \
    -md sha256 \
    -pass env:GHOS_EXPORT_PASSWORD \
    -in "$archive_path" \
    -out "$plain_archive"
unset encryption_password

mkdir -p "$export_root"
tar -xzf "$plain_archive" -C "$export_root"
for file_name in \
  roles.sql \
  schema.sql \
  data.sql \
  expected-counts.tsv \
  SHA256SUMS; do
  if [[ ! -s "$export_root/$file_name" ]]; then
    printf 'Archive is missing required file: %s\n' "$file_name" >&2
    exit 1
  fi
done
(
  cd "$export_root"
  shasum -a 256 -c SHA256SUMS
)

sed \
  's/^CREATE EXTENSION IF NOT EXISTS "pg_cron"/-- isolated rehearsal skips pg_cron: CREATE EXTENSION IF NOT EXISTS "pg_cron"/' \
  "$export_root/schema.sql" \
  > "$rehearsal_schema"

python3 - "$export_root/data.sql" "$rehearsal_data" <<'PY'
import sys
from pathlib import Path

source = Path(sys.argv[1])
target = Path(sys.argv[2])
header_prefix = 'COPY "auth"."custom_oauth_providers" '
column_fragment = ', "custom_claims_allowlist"'
lines = source.read_text(encoding="utf-8").splitlines(keepends=True)

inside_target = False
target_rows = 0
rewritten = False
output = []

for line in lines:
    if line.startswith(header_prefix):
        if column_fragment not in line:
            raise SystemExit("Expected production Auth column was not found.")
        line = line.replace(column_fragment, "", 1)
        inside_target = True
        rewritten = True
    elif inside_target and line.rstrip("\n") == r"\.":
        inside_target = False
    elif inside_target:
        target_rows += 1
    output.append(line)

if not rewritten:
    raise SystemExit("Auth compatibility header was not rewritten.")
if target_rows:
    raise SystemExit(
        "Refusing compatibility rewrite: auth.custom_oauth_providers "
        f"contains {target_rows} production row(s)."
    )

target.write_text("".join(output), encoding="utf-8")
PY

python3 - \
  "$export_root/expected-counts.tsv" \
  "$export_root/data.sql" \
  "$reconciled_counts" <<'PY'
import re
import sys
from pathlib import Path

manifest_path = Path(sys.argv[1])
data_path = Path(sys.argv[2])
output_path = Path(sys.argv[3])
known_drift = {"public.quote_tax_rate_cache"}

counts = {}
for raw_line in manifest_path.read_text(encoding="utf-8").splitlines():
    relation, count = raw_line.split("|", 1)
    counts[relation] = int(count)

data = data_path.read_text(encoding="utf-8")
copy_pattern = re.compile(
    r'^COPY "([^"]+)"\."([^"]+)" \([^\n]+\) FROM stdin;\n',
    re.MULTILINE,
)
copy_counts = {}
for match in copy_pattern.finditer(data):
    relation = f"{match.group(1)}.{match.group(2)}"
    terminator = data.find("\\.\n", match.end())
    if terminator < 0:
        raise SystemExit(f"COPY terminator missing for {relation}.")
    copy_counts[relation] = len(data[match.end():terminator].splitlines())

missing = sorted(known_drift - counts.keys())
unexpected = sorted(set(missing) - copy_counts.keys())
if unexpected:
    raise SystemExit(
        "Known production table is absent from the signed data export: "
        + ", ".join(unexpected)
    )

for relation in missing:
    counts[relation] = copy_counts[relation]
    print(
        f"Reconciled archived count manifest for {relation}: "
        f"{counts[relation]} row(s)."
    )

output_path.write_text(
    "".join(
        f"{relation}|{counts[relation]}\n"
        for relation in sorted(counts)
    ),
    encoding="utf-8",
)
PY

if database_exists "$database_name"; then
  printf 'Refusing to overwrite existing database %s.\n' "$database_name" >&2
  exit 1
fi

while IFS= read -r container_name; do
  [[ -n "$container_name" ]] && app_containers+=("$container_name")
done < <(
  docker ps --format '{{.Names}}' |
    grep -E '^(supabase-|realtime-dev\.)' |
    grep -v "^${db_container}$" |
    sort || true
)

printf '%s\n' 'Pausing local Supabase services for an isolated database clone...'
stop_application_services
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname = 'postgres'
  and pid <> pg_backend_pid();

create database $database_name
  with template postgres
  owner postgres;
SQL
database_created=1
start_application_services

printf '%s\n' 'Preparing isolated Auth, Storage, and public schemas...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$database_name" <<'SQL'
drop schema public cascade;
create schema public authorization pg_database_owner;
grant usage on schema public to public;
grant create on schema public to public;

do $$
declare
  target record;
begin
  for target in
    select n.nspname as schema_name, c.relname as table_name
    from pg_class c
    join pg_namespace n on n.oid = c.relnamespace
    where c.relkind in ('r', 'p')
      and n.nspname in ('auth', 'storage')
      and not (
        n.nspname = 'auth' and c.relname = 'schema_migrations'
      )
      and not (
        n.nspname = 'storage' and c.relname = 'migrations'
      )
  loop
    execute format(
      'truncate table %I.%I restart identity cascade',
      target.schema_name,
      target.table_name
    );
  end loop;
end
$$;
SQL

if [[ "${LOCAL_DELIVERY_APPLY_CLUSTER_ROLES:-0}" == 1 ]]; then
  printf '%s\n' 'Applying exported cluster-wide role settings by explicit request...'
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$database_name" \
    < "$export_root/roles.sql"
else
  printf '%s\n' \
    'Skipping cluster-wide role settings in the shared lab.' \
    'Set LOCAL_DELIVERY_APPLY_CLUSTER_ROLES=1 only for a disposable target cluster.'
fi

printf '%s\n' 'Restoring Local Delivery application schema...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  < "$rehearsal_schema"

printf '%s\n' 'Restoring Local Delivery production rows and Auth/Storage metadata...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$database_name" \
  < "$rehearsal_data"

LOCAL_DELIVERY_REHEARSAL_DATABASE="$database_name" \
LOCAL_DELIVERY_EXPECTED_COUNTS_FILE="$reconciled_counts" \
  "$repo_root/tools/verify_local_delivery_production_restore.sh"

if [[ "$retain_database" == "1" ]]; then
  printf '%s\n' \
    "Local Delivery restore rehearsal passed in $database_name." \
    'The isolated database was retained by explicit request.'
else
  drop_rehearsal_database
  printf '%s\n' \
    'Local Delivery restore rehearsal passed.' \
    'The disposable database and plaintext working files were removed.'
fi
