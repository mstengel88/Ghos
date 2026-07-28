#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stack_dir="$repo_root/migration/supabase/runtime/stack"
env_file="${SUPABASE_LAB_ENV_FILE:-$stack_dir/.env}"
base_url="${SUPABASE_LAB_URL:-http://127.0.0.1:8000}"
functions_container="${SUPABASE_FUNCTIONS_CONTAINER:-supabase-edge-functions}"
response_file="$(mktemp)"

if [[ "$base_url" != "http://localhost:"* \
      && "$base_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing Edge Function test for non-local URL: $base_url" >&2
  exit 1
fi
if [[ "$functions_container" != "supabase-edge-functions" ]]; then
  echo "Refusing unexpected functions container: $functions_container" >&2
  exit 1
fi

compose_base=(
  docker compose
  --env-file "$env_file"
  -f "$stack_dir/docker-compose.yml"
  -f "$stack_dir/docker-compose.pg17.yml"
  -f "$repo_root/migration/supabase/docker-compose.macos-storage.yml"
  -f "$repo_root/migration/supabase/docker-compose.mailpit.yml"
)

restore_local_delivery() {
  "${compose_base[@]}" \
    -f "$repo_root/migration/supabase/docker-compose.edge-functions.yml" \
    up -d --force-recreate functions >/dev/null 2>&1 || true
  rm -f "$response_file"
}
trap restore_local_delivery EXIT

anon_key="$(grep '^ANON_KEY=' "$env_file" | cut -d= -f2-)"
if [[ -z "$anon_key" ]]; then
  echo "The local anonymous key is missing." >&2
  exit 1
fi

"${compose_base[@]}" \
  -f "$repo_root/migration/supabase/docker-compose.winterwatch-functions.yml" \
  up -d --force-recreate functions

for attempt in {1..30}; do
  status="$(
    curl -sS -o /dev/null -w '%{http_code}' \
      -H "apikey: $anon_key" \
      "$base_url/functions/v1/home-assistant" 2>/dev/null || true
  )"
  [[ "$status" == "401" ]] && break
  sleep 1
done

request_status() {
  curl -sS -o "$response_file" -w '%{http_code}' \
    -H "apikey: $anon_key" \
    "$@"
}

assert_json_error() {
  local function_name="$1"
  local expected_status="$2"
  local expected_fragment="$3"
  shift 3
  local status
  status="$(request_status "$@" "$base_url/functions/v1/$function_name")"
  if [[ "$status" != "$expected_status" ]]; then
    echo "$function_name expected HTTP $expected_status, received $status" >&2
    cat "$response_file" >&2
    docker logs "$functions_container" --tail 120 >&2 || true
    exit 1
  fi
  jq -e --arg fragment "$expected_fragment" \
    '(.error // .message // "") | contains($fragment)' \
    "$response_file" >/dev/null
  echo "PASS: $function_name secret-free/auth guard"
}

assert_json_error \
  check-overtime 500 "Missing OneSignal environment variables" \
  -X POST -H "Content-Type: application/json" -d '{}'
assert_json_error \
  send-notification 500 "OneSignal not configured" \
  -X POST -H "Content-Type: application/json" -d '{}'
assert_json_error export-to-drive 401 "Unauthorized" -X POST
assert_json_error home-assistant 401 "Unauthorized"
assert_json_error notify-maintenance-request 401 "Missing authorization" -X POST
assert_json_error overtime-action 401 "Missing authorization header" -X POST
assert_json_error \
  get-weather 400 "Latitude and longitude are required" \
  -X POST -H "Content-Type: application/json" -d '{}'

if docker logs "$functions_container" --since 5m 2>&1 \
    | grep -Eiq 'panic|segmentation fault|worker boot error'; then
  echo "The Edge Runtime logged a worker failure." >&2
  exit 1
fi

echo "WinterWatch secret-free Edge Function acceptance passed."
