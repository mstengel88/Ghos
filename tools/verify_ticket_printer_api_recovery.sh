#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
source_root="${TICKET_PRINTER_SOURCE_ROOT:-/Users/mattstengel/edit-my-ticket}"
migration_dir="$source_root/supabase/migrations"
candidate_migration="$repo_root/migration/supabase/candidates/ticket-printer/000_live_dispatch_bridge.sql"
api_grants_migration="$repo_root/migration/supabase/candidates/ticket-printer/001_api_grants.sql"
runtime_env="$repo_root/migration/supabase/runtime/stack/.env"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
api_base="${SUPABASE_LOCAL_API_URL:-http://127.0.0.1:8000}"
scheduler_migration="20260511173000_schedule_loadrite_sync_every_5_minutes.sql"
timestamp="$(date -u +%Y%m%d_%H%M%S)"
candidate_database="ticket_printer_api_${timestamp}"
holding_database="local_delivery_before_ticket_printer_api_${timestamp}"
backup_path="${TMPDIR:-/tmp}/ghos-local-delivery-before-ticket-printer-api-${timestamp}.dump"
test_email="ghos-ticket-printer-api-${timestamp}@example.invalid"
test_password="$(openssl rand -base64 36 | tr -d '\n')"
service_role_key=""
anon_key=""
test_user_id=""
access_token=""
candidate_created=0
swapped=0
services_stopped=0
app_containers=()

read_env_value() {
  python3 - "$runtime_env" "$1" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
key = sys.argv[2]
for raw_line in path.read_text(encoding="utf-8").splitlines():
    line = raw_line.strip()
    if not line or line.startswith("#") or "=" not in line:
        continue
    name, value = line.split("=", 1)
    if name == key:
        print(value)
        raise SystemExit(0)
raise SystemExit(f"Missing {key} in {path}")
PY
}

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

restore_local_delivery() {
  stop_application_services
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname in ('postgres', '$holding_database')
  and pid <> pg_backend_pid();

alter database postgres rename to $candidate_database;
alter database $holding_database rename to postgres;
SQL
  swapped=0
  candidate_created=1
  start_application_services
}

cleanup() {
  set +e
  if [[ -n "$test_user_id" && "$swapped" == 1 ]]; then
    curl -fsS \
      -X DELETE \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/auth/v1/admin/users/$test_user_id" \
      >/dev/null
  fi
  if [[ "$swapped" == 1 ]]; then
    restore_local_delivery
  elif [[ "$services_stopped" == 1 ]]; then
    start_application_services
  fi
  if [[ "$candidate_created" == 1 ]] && database_exists "$candidate_database"; then
    docker exec "$db_container" \
      dropdb -U "$database_admin" --if-exists "$candidate_database" \
      >/dev/null 2>&1
  fi
  unset service_role_key anon_key test_password access_token
}
trap cleanup EXIT

for command_name in curl docker jq openssl python3; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ "$api_base" != "http://localhost:"* \
      && "$api_base" != "http://127.0.0.1:"* ]]; then
  printf 'Refusing API recovery test for non-local URL: %s\n' "$api_base" >&2
  exit 1
fi
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi
if [[ ! -d "$migration_dir" ]]; then
  printf 'Ticket Printer migration directory not found: %s\n' \
    "$migration_dir" >&2
  exit 1
fi
if [[ ! -s "$candidate_migration" \
      || ! -s "$api_grants_migration" \
      || ! -s "$runtime_env" ]]; then
  printf '%s\n' 'Ticket Printer candidate or local runtime environment is missing.' >&2
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
if database_exists "$candidate_database" || database_exists "$holding_database"; then
  printf '%s\n' 'A temporary Ticket Printer recovery database already exists.' >&2
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
if ((${#app_containers[@]} == 0)); then
  printf '%s\n' 'No running local Supabase application services were found.' >&2
  exit 1
fi

service_role_key="$(read_env_value SERVICE_ROLE_KEY)"
anon_key="$(read_env_value ANON_KEY)"
if [[ -z "$service_role_key" || -z "$anon_key" ]]; then
  printf '%s\n' 'The local API keys are empty.' >&2
  exit 1
fi

printf 'Creating a private safety dump of the current local database at %s...\n' \
  "$backup_path"
docker exec "$db_container" \
  pg_dump -U postgres -d postgres -Fc >"$backup_path"
chmod 600 "$backup_path"
if [[ ! -s "$backup_path" ]]; then
  printf '%s\n' 'The pre-rehearsal safety dump is empty.' >&2
  exit 1
fi

printf '%s\n' 'Cloning the local Supabase platform database...'
stop_application_services
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname = 'postgres'
  and pid <> pg_backend_pid();

create database $candidate_database
  with template postgres
  owner $database_admin;
SQL
candidate_created=1
start_application_services

printf '%s\n' 'Building the Ticket Printer application schema in the clone...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$candidate_database" \
  >/dev/null <<'SQL'
drop schema public cascade;
create schema public authorization postgres;
grant usage on schema public to postgres, anon, authenticated, service_role;
grant all on schema public to postgres, service_role;

insert into auth.users (id, email)
values
  (
    '46c4375d-6d33-4962-9c77-c63fe1ba71d1',
    'rehearsal-admin-one@example.invalid'
  ),
  (
    'b9438690-e79b-4f4a-9d70-657c52e00588',
    'rehearsal-admin-two@example.invalid'
  ),
  (
    '48adee4d-50d0-4d88-be9e-f3930ba898db',
    'rehearsal-user@example.invalid'
  )
on conflict (id) do nothing;
SQL

migration_count=0
infrastructure_migration_count=0
for migration in "$migration_dir"/*.sql; do
  [[ -f "$migration" ]] || continue
  if [[ "$(basename "$migration")" == "$scheduler_migration" ]]; then
    infrastructure_migration_count=$((infrastructure_migration_count + 1))
    continue
  fi
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$candidate_database" \
    <"$migration" >/dev/null
  migration_count=$((migration_count + 1))
done
if [[ "$migration_count" -ne 38 || "$infrastructure_migration_count" -ne 1 ]]; then
  printf 'Unexpected migration split: %s schema, %s infrastructure.\n' \
    "$migration_count" "$infrastructure_migration_count" >&2
  exit 1
fi
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$candidate_database" \
  <"$candidate_migration" >/dev/null
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$candidate_database" \
  <"$api_grants_migration" >/dev/null

contract="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$candidate_database" -Atqc \
    "select
       (select count(*) from information_schema.tables
        where table_schema = 'public' and table_type = 'BASE TABLE'),
       (select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'public' and c.relkind = 'r' and c.relrowsecurity),
       (select count(*) from pg_policies where schemaname = 'public');"
)"
if [[ "$contract" != "14|14|53" ]]; then
  printf 'Unexpected Ticket Printer schema contract: %s\n' "$contract" >&2
  exit 1
fi

printf '%s\n' 'Temporarily activating the Ticket Printer candidate...'
stop_application_services
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname in ('postgres', '$candidate_database')
  and pid <> pg_backend_pid();

alter database postgres rename to $holding_database;
alter database $candidate_database rename to postgres;
SQL
swapped=1
candidate_created=0
start_application_services

printf '%s\n' 'Waiting for Auth and PostgREST to accept the candidate...'
for _ in $(seq 1 90); do
  if curl -fsS \
      -H "apikey: $service_role_key" \
      "$api_base/auth/v1/settings" >/dev/null 2>&1 &&
    curl -fsS \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/rest/v1/tickets?select=id&limit=1" \
      >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/rest/v1/tickets?select=id&limit=1" \
  >/dev/null

printf '%s\n' 'Testing Ticket Printer Auth and profile provisioning...'
create_response="$(
  jq -n \
    --arg email "$test_email" \
    --arg password "$test_password" \
    '{email: $email, password: $password, email_confirm: true}' |
    curl -fsS \
      -X POST \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      -H 'Content-Type: application/json' \
      --data-binary @- \
      "$api_base/auth/v1/admin/users"
)"
test_user_id="$(jq -er '.id' <<<"$create_response")"
unset create_response

login_response="$(
  jq -n \
    --arg email "$test_email" \
    --arg password "$test_password" \
    '{email: $email, password: $password}' |
    curl -fsS \
      -X POST \
      -H "apikey: $anon_key" \
      -H 'Content-Type: application/json' \
      --data-binary @- \
      "$api_base/auth/v1/token?grant_type=password"
)"
access_token="$(jq -er '.access_token' <<<"$login_response")"
unset login_response

profile_response="$(
  curl -fsS \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $access_token" \
    "$api_base/rest/v1/profiles?select=user_id&user_id=eq.$test_user_id"
)"
jq -e --arg id "$test_user_id" \
  'length == 1 and .[0].user_id == $id' \
  <<<"$profile_response" >/dev/null
unset profile_response access_token

role_count="$(
  curl -fsS \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    "$api_base/rest/v1/user_roles?select=id&user_id=eq.$test_user_id" |
    jq 'length'
)"
if [[ "$role_count" -ne 1 ]]; then
  printf 'Expected one default Ticket Printer role, found %s.\n' \
    "$role_count" >&2
  exit 1
fi

curl -fsS \
  -X DELETE \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/auth/v1/admin/users/$test_user_id" \
  >/dev/null
test_user_id=""

printf '%s\n' \
  'Ticket Printer clean-room Auth and PostgREST recovery acceptance passed.' \
  'Restoring the Local-Delivery compatibility database and services...'
restore_local_delivery

printf '%s\n' \
  'Local Supabase services were restored successfully.' \
  "Private safety dump retained at: $backup_path"
