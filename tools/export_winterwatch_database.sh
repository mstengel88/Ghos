#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"

project_ref="${SUPABASE_EXPORT_PROJECT_REF:-caegybyfdkmgjrygnavg}"
pooler_host="${SUPABASE_EXPORT_POOLER_HOST:-aws-1-us-east-1.pooler.supabase.com}"
export_label="${SUPABASE_EXPORT_LABEL:-WinterWatch}"
archive_slug="${SUPABASE_EXPORT_ARCHIVE_SLUG:-winterwatch-pro}"
expected_counts_sql_file="${SUPABASE_EXPORT_EXPECTED_COUNTS_SQL_FILE:-}"
supabase_cli_commit="ac24960aeccfd7b2cfc0e59629c732f03f1a55a8"
postgres_image="public.ecr.aws/supabase/postgres:17.6.1.147"
keychain_service="${SUPABASE_KEYCHAIN_SERVICE:-Supabase CLI}"
keychain_account="${SUPABASE_KEYCHAIN_ACCOUNT:-supabase}"
archive_keychain_service="GHOS Migration Export Encryption"
archive_keychain_account="${SUPABASE_EXPORT_KEYCHAIN_ACCOUNT:-winterwatch-pro}"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
destination_root="${SUPABASE_DATABASE_EXPORT_ROOT:-${WINTERWATCH_DATABASE_EXPORT_ROOT:-$repo_root/migration/supabase/exports/$archive_slug/$timestamp}}"
encrypted_archive="$destination_root/$archive_slug-database.sql.tar.gz.enc"
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-managed-export.XXXXXX")"
export_root="$work_root/export"
scripts_root="$work_root/scripts"
plain_archive="$work_root/$archive_slug-database.sql.tar.gz"
verify_archive="$work_root/$archive_slug-verify.tar.gz"
pat=""
encryption_password=""

cleanup() {
  unset pat encryption_password
  rm -rf "$work_root"
}
trap cleanup EXIT

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$1" >&2
    exit 1
  fi
}

for command_name in security docker curl openssl tar shasum git base64 cp; do
  require_command "$command_name"
done

if ! docker info >/dev/null 2>&1; then
  printf '%s\n' 'Docker is not available. Start Docker Desktop and retry.' >&2
  exit 1
fi

keychain_value="$(
  security find-generic-password \
    -s "$keychain_service" \
    -a "$keychain_account" \
    -w
)"
if [[ "$keychain_value" == go-keyring-base64:* ]]; then
  if base64 --help >/dev/null 2>&1; then
    pat="$(printf '%s' "${keychain_value#*:}" | base64 --decode)"
  else
    pat="$(printf '%s' "${keychain_value#*:}" | base64 -D)"
  fi
else
  pat="$keychain_value"
fi
unset keychain_value

if [[ -z "$pat" ]]; then
  printf '%s\n' 'Supabase CLI token was not found in macOS Keychain.' >&2
  exit 1
fi

if ! docker run --rm \
  -e PGPASSWORD="$pat" \
  -e PGOPTIONS="-c jit=true" \
  -e PGHOST="$pooler_host" \
  -e PGPORT=5432 \
  -e PGUSER="postgres.$project_ref" \
  -e PGDATABASE=postgres \
  "$postgres_image" \
  psql -v ON_ERROR_STOP=1 -Atc 'select 1' \
  >/dev/null 2>&1; then
  printf '%s\n' \
    "Supabase temporary database access is not ready for $export_label." \
    "Enable temporary access for project $project_ref and map the current Supabase user to the postgres role." \
    'No database password was reset and no production data was changed.' >&2
  exit 1
fi

mkdir -p "$export_root" "$scripts_root"

download_dump_script() {
  local script_name="$1"
  local expected_hash="$2"
  local script_path="$scripts_root/$script_name"
  local actual_hash

  curl -fsSL \
    "https://raw.githubusercontent.com/supabase/cli/$supabase_cli_commit/apps/cli-go/pkg/migration/scripts/$script_name" \
    -o "$script_path"

  actual_hash="$(shasum -a 256 "$script_path" | awk '{print $1}')"
  if [[ "$actual_hash" != "$expected_hash" ]]; then
    printf 'Hash mismatch for pinned Supabase script %s.\n' "$script_name" >&2
    exit 1
  fi
  chmod 700 "$script_path"
}

download_dump_script \
  dump_role.sh \
  cc442e4f19d349b7db1dde69913ed06ef20f68198391d9beef33f63493cf26c8
download_dump_script \
  dump_schema.sh \
  5cd57189f6565ddf651ff149995398a4c9b1971ca34a0093a77c011a41f21d64
download_dump_script \
  dump_data.sh \
  c943c7a926122ea0649ddd4bf9b8fb9b12bed23ac9f33da0b489ac83e687d241

reserved_roles='anon|authenticated|authenticator|cli_login_.*|dashboard_user|pgbouncer|postgres|service_role|supabase_.*|pgsodium_keyholder|pgsodium_keyiduser|pgsodium_keymaker|pgtle_admin'
allowed_configs='pgaudit.*|pgrst.*|session_replication_role|statement_timeout|track_io_timing'
schema_exclusions='information_schema|pg_*|_analytics|_realtime|_supavisor|auth|etl|extensions|pgbouncer|realtime|storage|supabase_functions|supabase_migrations|cron|dbdev|graphql|graphql_public|net|pgmq|pgsodium|pgsodium_masks|pgtle|repack|tiger|tiger_data|timescaledb_*|_timescaledb_*|topology|vault'
data_exclusions='information_schema|pg_*|graphql|graphql_public|pgsodium|pgsodium_masks|pgtle|repack|tiger|tiger_data|timescaledb_*|_timescaledb_*|topology|vault|etl|extensions|pgbouncer|realtime|supabase_migrations|_analytics|_realtime|_supavisor'

run_dump() {
  local script_name="$1"
  local output_name="$2"
  shift 2

  docker run --rm \
    -e PGPASSWORD="$pat" \
    -e PGOPTIONS="-c jit=true" \
    -e PGHOST="$pooler_host" \
    -e PGPORT=5432 \
    -e PGUSER="postgres.$project_ref" \
    -e PGDATABASE=postgres \
    "$@" \
    -v "$export_root:/export" \
    -v "$scripts_root:/scripts:ro" \
    "$postgres_image" \
    bash -c "/scripts/$script_name > /export/$output_name"
}

printf 'Exporting %s role settings...\n' "$export_label"
run_dump dump_role.sh roles.sql \
  -e RESERVED_ROLES="$reserved_roles" \
  -e ALLOWED_CONFIGS="$allowed_configs" \
  -e EXTRA_SED='/^--/d'

printf 'Exporting %s application schema...\n' "$export_label"
run_dump dump_schema.sh schema.sql \
  -e EXCLUDED_SCHEMAS="$schema_exclusions" \
  -e EXTRA_SED='/^--/d'

printf 'Exporting %s database rows and Auth/Storage metadata...\n' "$export_label"
run_dump dump_data.sh data.sql \
  -e INCLUDED_SCHEMAS='*' \
  -e EXCLUDED_SCHEMAS="$data_exclusions"

if [[ -n "$expected_counts_sql_file" ]]; then
  if [[ ! -f "$expected_counts_sql_file" ]]; then
    printf 'Expected-count SQL file not found: %s\n' \
      "$expected_counts_sql_file" >&2
    exit 1
  fi
  cp "$expected_counts_sql_file" "$scripts_root/expected_counts.sql"
else
  cat > "$scripts_root/expected_counts.sql" <<'SQL'
select 'auth.users', count(*) from auth.users
union all select 'auth.identities', count(*) from auth.identities
union all select 'public.accounts', count(*) from public.accounts
union all select 'public.audit_logs', count(*) from public.audit_logs
union all select 'public.employee_locations', count(*) from public.employee_locations
union all select 'public.employees', count(*) from public.employees
union all select 'public.equipment', count(*) from public.equipment
union all select 'public.maintenance_logs', count(*) from public.maintenance_logs
union all select 'public.maintenance_notification_settings', count(*) from public.maintenance_notification_settings
union all select 'public.maintenance_requests', count(*) from public.maintenance_requests
union all select 'public.notification_preferences', count(*) from public.notification_preferences
union all select 'public.notification_types', count(*) from public.notification_types
union all select 'public.notifications_log', count(*) from public.notifications_log
union all select 'public.overtime_notification_settings', count(*) from public.overtime_notification_settings
union all select 'public.overtime_notifications_sent', count(*) from public.overtime_notifications_sent
union all select 'public.profiles', count(*) from public.profiles
union all select 'public.push_device_tokens', count(*) from public.push_device_tokens
union all select 'public.scheduled_notifications', count(*) from public.scheduled_notifications
union all select 'public.shovel_work_logs', count(*) from public.shovel_work_logs
union all select 'public.time_clock', count(*) from public.time_clock
union all select 'public.user_roles', count(*) from public.user_roles
union all select 'public.work_logs', count(*) from public.work_logs
union all select 'storage.buckets', count(*) from storage.buckets
union all select 'storage.objects', count(*) from storage.objects
order by 1;
SQL
fi

printf '%s\n' 'Capturing exact reconciliation counts for this snapshot...'
docker run --rm \
  -e PGPASSWORD="$pat" \
  -e PGOPTIONS="-c jit=true" \
  -e PGHOST="$pooler_host" \
  -e PGPORT=5432 \
  -e PGUSER="postgres.$project_ref" \
  -e PGDATABASE=postgres \
  -v "$export_root:/export" \
  -v "$scripts_root:/scripts:ro" \
  "$postgres_image" \
  psql -v ON_ERROR_STOP=1 -At -F '|' \
    -f /scripts/expected_counts.sql \
    -o /export/expected-counts.tsv

unset pat

for file_name in roles.sql schema.sql data.sql expected-counts.tsv; do
  if [[ ! -s "$export_root/$file_name" ]]; then
    printf 'Export validation failed: %s is empty.\n' "$file_name" >&2
    exit 1
  fi
done

(
  cd "$export_root"
  shasum -a 256 \
    roles.sql schema.sql data.sql expected-counts.tsv \
    > SHA256SUMS
  shasum -a 256 -c SHA256SUMS
)

if security find-generic-password \
  -s "$archive_keychain_service" \
  -a "$archive_keychain_account" \
  >/dev/null 2>&1; then
  encryption_password="$(
    security find-generic-password \
      -s "$archive_keychain_service" \
      -a "$archive_keychain_account" \
      -w
  )"
else
  encryption_password="$(openssl rand -base64 48)"
  security add-generic-password \
    -U \
    -s "$archive_keychain_service" \
    -a "$archive_keychain_account" \
    -w "$encryption_password" \
    >/dev/null
fi

mkdir -p "$destination_root"
tar -C "$export_root" -czf "$plain_archive" \
  roles.sql schema.sql data.sql expected-counts.tsv SHA256SUMS

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
    -out "$verify_archive"

tar -tzf "$verify_archive" >/dev/null
(
  cd "$destination_root"
  shasum -a 256 "$(basename "$encrypted_archive")" \
    > "$(basename "$encrypted_archive").sha256"
)
chmod 600 "$encrypted_archive" "$encrypted_archive.sha256"

if ! git -C "$repo_root" check-ignore -q "$encrypted_archive"; then
  printf '%s\n' 'Encrypted export is not ignored by Git; stopping.' >&2
  exit 1
fi

printf '%s\n' \
  "Encrypted $export_label database/Auth export completed and verified." \
  "Archive: $encrypted_archive" \
  "Checksum: $encrypted_archive.sha256" \
  "Encryption key: macOS Keychain service '$archive_keychain_service', account '$archive_keychain_account'"
