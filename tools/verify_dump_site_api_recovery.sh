#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
source_root="${DUMP_SITE_SOURCE_ROOT:-/Users/mattstengel/Documents/GreenHills APP}"
migration_dir="$source_root/supabase/migrations"
runtime_env="$repo_root/migration/supabase/runtime/stack/.env"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
api_base="${SUPABASE_LOCAL_API_URL:-http://127.0.0.1:8000}"
timestamp="$(date -u +%Y%m%d_%H%M%S)"
candidate_database="dump_site_api_${timestamp}"
holding_database="local_delivery_before_dump_site_api_${timestamp}"
backup_path="${TMPDIR:-/tmp}/ghos-local-delivery-before-dump-site-api-${timestamp}.dump"
service_role_key=""
anon_key=""
entry_id=""
claim_token=""
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
  if [[ -n "$entry_id" && "$swapped" == 1 ]]; then
    curl -fsS \
      -X DELETE \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/rest/v1/dump_site_entries?id=eq.$entry_id" \
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
  unset service_role_key anon_key claim_token
}
trap cleanup EXIT

for command_name in curl docker jq python3; do
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
if [[ ! -d "$migration_dir" || ! -s "$runtime_env" ]]; then
  printf '%s\n' 'Dump Site migrations or local runtime environment are missing.' >&2
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
  printf '%s\n' 'A temporary Dump Site recovery database already exists.' >&2
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

printf '%s\n' 'Building the Dump Site application schema in the clone...'
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d "$candidate_database" \
  >/dev/null <<'SQL'
drop schema public cascade;
create schema public authorization postgres;
grant usage on schema public to postgres, anon, authenticated, service_role;
grant all on schema public to postgres, service_role;
SQL

migration_count=0
for migration in "$migration_dir"/*.sql; do
  [[ -f "$migration" ]] || continue
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$candidate_database" \
    <"$migration" >/dev/null
  migration_count=$((migration_count + 1))
done
if [[ "$migration_count" -ne 8 ]]; then
  printf 'Expected eight Dump Site migrations, applied %s.\n' \
    "$migration_count" >&2
  exit 1
fi

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
if [[ "$contract" != "3|3|0" ]]; then
  printf 'Unexpected Dump Site schema contract: %s\n' "$contract" >&2
  exit 1
fi

printf '%s\n' 'Temporarily activating the Dump Site candidate...'
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

printf '%s\n' 'Waiting for PostgREST to accept the candidate...'
for _ in $(seq 1 90); do
  if curl -fsS \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/rest/v1/dump_site_entries?select=id&limit=1" \
      >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/rest/v1/dump_site_entries?select=id&limit=1" \
  >/dev/null

printf '%s\n' 'Verifying that browser roles cannot read Dump Site data...'
anon_status="$(
  curl -sS -o /dev/null -w '%{http_code}' \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    "$api_base/rest/v1/dump_site_entries?select=id&limit=1"
)"
if [[ "$anon_status" != "401" && "$anon_status" != "403" ]]; then
  printf 'Expected anon API denial, received HTTP %s.\n' "$anon_status" >&2
  exit 1
fi

printf '%s\n' 'Testing the service-only Dump Site queue workflow through PostgREST...'
entry_response="$(
  jq -n '{
    access_source: "qr",
    shopify_company_id: "api-rehearsal-company",
    company_name: "API Rehearsal Company",
    truck_number: "TEST-TRUCK",
    driver_name: "API Rehearsal Driver",
    material_type: "Clean Fill",
    vehicle_type: "Pickup Truck",
    modern_retail_status: "disabled"
  }' |
    curl -fsS \
      -X POST \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      -H 'Content-Type: application/json' \
      -H 'Prefer: return=representation' \
      --data-binary @- \
      "$api_base/rest/v1/dump_site_entries?select=id,confirmation_id,counterpoint_bridge_status"
)"
entry_id="$(jq -er '.[0].id' <<<"$entry_response")"
confirmation_id="$(jq -er '.[0].confirmation_id' <<<"$entry_response")"
queue_status="$(jq -er '.[0].counterpoint_bridge_status' <<<"$entry_response")"
if [[ "$confirmation_id" != "201-D10000" || "$queue_status" != "queued" ]]; then
  printf 'Unexpected inserted Dump Site entry: %s / %s.\n' \
    "$confirmation_id" "$queue_status" >&2
  exit 1
fi

claim_response="$(
  jq -n '{
    p_bridge_id: "api-rehearsal",
    p_limit: 1,
    p_max_attempts: 5,
    p_claim_seconds: 180
  }' |
    curl -fsS \
      -X POST \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      -H 'Content-Type: application/json' \
      --data-binary @- \
      "$api_base/rest/v1/rpc/claim_dump_site_counterpoint_bridge"
)"
claimed_entry_id="$(jq -er '.[0].id' <<<"$claim_response")"
claim_token="$(jq -er '.[0].claim_token' <<<"$claim_response")"
if [[ "$claimed_entry_id" != "$entry_id" ]]; then
  printf '%s\n' 'The service API claimed an unexpected Dump Site entry.' >&2
  exit 1
fi

completion_response="$(
  jq -n \
    --arg entry_id "$entry_id" \
    --arg claim_token "$claim_token" \
    '{
      p_entry_id: $entry_id,
      p_bridge_id: "api-rehearsal",
      p_claim_token: $claim_token,
      p_status: "created",
      p_ticket_number: "API-REHEARSAL-1",
      p_error: null
    }' |
    curl -fsS \
      -X POST \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      -H 'Content-Type: application/json' \
      --data-binary @- \
      "$api_base/rest/v1/rpc/complete_dump_site_counterpoint_bridge"
)"
if [[ "$(jq -r '.' <<<"$completion_response")" != "true" ]]; then
  printf '%s\n' 'The service API did not complete the Dump Site claim.' >&2
  exit 1
fi

final_status="$(
  curl -fsS \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    "$api_base/rest/v1/dump_site_entries?select=counterpoint_bridge_status,counterpoint_ticket_number&id=eq.$entry_id" |
    jq -r '.[0] | "\(.counterpoint_bridge_status)|\(.counterpoint_ticket_number)"'
)"
if [[ "$final_status" != "created|API-REHEARSAL-1" ]]; then
  printf 'Unexpected completed Dump Site status: %s\n' "$final_status" >&2
  exit 1
fi

curl -fsS \
  -X DELETE \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/rest/v1/dump_site_entries?id=eq.$entry_id" \
  >/dev/null
entry_id=""

printf '%s\n' \
  'Dump Site clean-room PostgREST recovery acceptance passed.' \
  'Restoring the Local-Delivery compatibility database and services...'
restore_local_delivery

printf '%s\n' \
  'Local Supabase services were restored successfully.' \
  "Private safety dump retained at: $backup_path"
