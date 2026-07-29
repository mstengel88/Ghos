#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="${WINTERWATCH_REHEARSAL_DATABASE:-winterwatch_rehearsal_$(date -u +%Y%m%d_%H%M%S)}"
archive_path="${WINTERWATCH_DATABASE_ARCHIVE:-}"
keychain_service="GHOS Migration Export Encryption"
keychain_account="winterwatch-pro"
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-winterwatch-restore.XXXXXX")"
export_root="$work_root/export"
plain_archive="$work_root/winterwatch-pro-database.sql.tar.gz"
rehearsal_schema="$work_root/schema.rehearsal.sql"
rehearsal_data="$work_root/data.rehearsal.sql"
encryption_password=""
services_stopped=0
app_containers=()

cleanup() {
  if [[ "$services_stopped" == 1 ]] && ((${#app_containers[@]})); then
    docker start "${app_containers[@]}" >/dev/null || true
  fi
  unset encryption_password
  rm -rf "$work_root"
}
trap cleanup EXIT

if [[ ! "$database_name" =~ ^[a-z_][a-z0-9_]*$ ]]; then
  printf 'Unsafe database name: %s\n' "$database_name" >&2
  exit 1
fi

if [[ -z "$archive_path" ]]; then
  archive_path="$(
    find "$repo_root/migration/supabase/exports/winterwatch-pro" \
      -type f -name 'winterwatch-pro-database.sql.tar.gz.enc' \
      -print 2>/dev/null |
      sort |
      tail -n 1
  )"
fi
if [[ -z "$archive_path" || ! -s "$archive_path" ]]; then
  printf '%s\n' \
    'No encrypted WinterWatch database archive was found.' \
    'Set WINTERWATCH_DATABASE_ARCHIVE to the archive path.' >&2
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
for file_name in roles.sql schema.sql data.sql SHA256SUMS; do
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

if docker exec "$db_container" \
  psql -U postgres -d postgres -Atqc \
    "select 1 from pg_database where datname = '$database_name'" |
  grep -qx 1; then
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
if ((${#app_containers[@]})); then
  docker stop "${app_containers[@]}" >/dev/null
  services_stopped=1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U supabase_admin -d postgres <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname = 'postgres'
  and pid <> pg_backend_pid();

create database $database_name
  with template postgres
  owner postgres;
SQL

if ((${#app_containers[@]})); then
  docker start "${app_containers[@]}" >/dev/null
  services_stopped=0
fi

printf '%s\n' 'Preparing isolated Auth, Storage, and public schemas...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U supabase_admin -d "$database_name" <<'SQL'
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

if [[ "${WINTERWATCH_APPLY_CLUSTER_ROLES:-0}" == 1 ]]; then
  printf '%s\n' 'Applying exported cluster-wide role settings by explicit request...'
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U supabase_admin -d "$database_name" \
    < "$export_root/roles.sql"
else
  printf '%s\n' \
    'Skipping cluster-wide role settings in the shared lab.' \
    'Set WINTERWATCH_APPLY_CLUSTER_ROLES=1 only for a disposable target cluster.'
fi

printf '%s\n' 'Restoring WinterWatch application schema...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  < "$rehearsal_schema"

printf '%s\n' 'Restoring WinterWatch production rows and Auth/Storage metadata...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U supabase_admin -d "$database_name" \
  < "$rehearsal_data"

WINTERWATCH_REHEARSAL_DATABASE="$database_name" \
WINTERWATCH_EXPECTED_COUNTS_FILE="$(
  if [[ -s "$export_root/expected-counts.tsv" ]]; then
    printf '%s' "$export_root/expected-counts.tsv"
  fi
)" \
  "$repo_root/tools/verify_winterwatch_restore.sh"

printf '%s\n' \
  "WinterWatch restore rehearsal completed in $database_name." \
  'The isolated rehearsal database was retained for inspection.'
