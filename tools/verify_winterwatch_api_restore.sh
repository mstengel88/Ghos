#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
runtime_env="$repo_root/migration/supabase/runtime/stack/.env"
storage_manifest="$repo_root/migration/supabase/exports/storage/winterwatch/initial/manifest.json"
database_name="${WINTERWATCH_REHEARSAL_DATABASE:-}"
timestamp="$(date -u +%Y%m%d_%H%M%S)"
holding_database="local_delivery_before_winterwatch_api_$timestamp"
backup_path="${TMPDIR:-/tmp}/ghos-local-delivery-before-winterwatch-api-$timestamp.dump"
api_base="${SUPABASE_LOCAL_API_URL:-http://127.0.0.1:8000}"
test_email="ghos-winterwatch-api-$timestamp@example.invalid"
test_password="$(openssl rand -base64 36 | tr -d '\n')"
service_role_key=""
test_user_id=""
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
        docker start "$container_name" >/dev/null
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

swap_databases_back() {
  stop_application_services
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname in ('postgres', '$holding_database')
  and pid <> pg_backend_pid();

alter database postgres rename to $database_name;
alter database $holding_database rename to postgres;
SQL
  swapped=0
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
    swap_databases_back
  elif [[ "$services_stopped" == 1 ]]; then
    start_application_services
  fi
  unset service_role_key test_password
}
trap cleanup EXIT

for command_name in curl docker jq openssl python3 shasum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ ! -s "$runtime_env" ]]; then
  printf 'Local Supabase runtime environment is missing: %s\n' "$runtime_env" >&2
  exit 1
fi
if [[ ! -s "$storage_manifest" ]]; then
  printf 'WinterWatch Storage manifest is missing: %s\n' "$storage_manifest" >&2
  exit 1
fi
if ! docker inspect "$db_container" >/dev/null 2>&1; then
  printf 'Local Supabase database container is unavailable: %s\n' "$db_container" >&2
  exit 1
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
if ! database_exists "$database_name"; then
  printf 'WinterWatch rehearsal database does not exist: %s\n' \
    "$database_name" >&2
  exit 1
fi
if database_exists "$holding_database"; then
  printf 'Temporary holding database already exists: %s\n' \
    "$holding_database" >&2
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
if [[ -z "$service_role_key" ]]; then
  printf '%s\n' 'The local service-role key is empty.' >&2
  exit 1
fi

printf 'Creating a private safety dump of the current local database at %s...\n' \
  "$backup_path"
docker exec "$db_container" \
  pg_dump -U postgres -d postgres -Fc > "$backup_path"
chmod 600 "$backup_path"
if [[ ! -s "$backup_path" ]]; then
  printf '%s\n' 'The pre-rehearsal safety dump is empty.' >&2
  exit 1
fi

printf 'Temporarily activating %s for local API verification...\n' "$database_name"
stop_application_services
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname in ('postgres', '$database_name')
  and pid <> pg_backend_pid();

alter database postgres rename to $holding_database;
alter database $database_name rename to postgres;
SQL
swapped=1
start_application_services

printf '%s\n' 'Waiting for the local Supabase API services...'
for _ in $(seq 1 90); do
  if curl -fsS \
      -H "apikey: $service_role_key" \
      "$api_base/auth/v1/settings" >/dev/null 2>&1 &&
    curl -fsS \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/rest/v1/accounts?select=id&limit=1" \
      >/dev/null 2>&1 &&
    curl -fsS \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$api_base/storage/v1/bucket" \
      >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

curl -fsS \
  -H "apikey: $service_role_key" \
  "$api_base/auth/v1/settings" >/dev/null
curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/rest/v1/accounts?select=id&limit=1" \
  >/dev/null

printf '%s\n' 'Testing the Auth administrator lifecycle...'
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
curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/auth/v1/admin/users/$test_user_id" |
  jq -e --arg id "$test_user_id" '.id == $id' >/dev/null

printf '%s\n' 'Testing the restored private Storage bucket and one object...'
curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/storage/v1/bucket" |
  jq -e 'any(.[]; .id == "work-photos" and .public == false)' >/dev/null

IFS='|' read -r object_name expected_hash < <(
  python3 - "$storage_manifest" <<'PY'
import json
import sys

manifest = json.load(open(sys.argv[1], encoding="utf-8"))
obj = manifest["objects"][0]
print(f'{obj["name"]}|{obj["sha256"]}')
PY
)
encoded_object="$(
  python3 - "$object_name" <<'PY'
import sys
from urllib.parse import quote

print(quote(sys.argv[1], safe="/"))
PY
)"
download_path="${TMPDIR:-/tmp}/ghos-winterwatch-storage-sample-$timestamp"
curl -fsS \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  "$api_base/storage/v1/object/authenticated/work-photos/$encoded_object" \
  -o "$download_path"
actual_hash="$(shasum -a 256 "$download_path" | awk '{print $1}')"
rm -f "$download_path"
if [[ "$actual_hash" != "$expected_hash" ]]; then
  printf '%s\n' 'Restored Storage object hash verification failed.' >&2
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
  'WinterWatch Auth, PostgREST, and private Storage API verification passed.' \
  'Restoring the Local-Delivery compatibility database and services...'
swap_databases_back

printf '%s\n' \
  'Local Supabase services were restored successfully.' \
  "Private safety dump retained at: $backup_path"
