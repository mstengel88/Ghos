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

expected_sync_hash="dc123a27738311f3712234bce1a0592f76ca0740a7fc70af1d2d1249466775e1"
actual_sync_hash="$(
  sha256sum \
    "$repo_root/migration/supabase/baselines/ticket-printer/functions/loadrite-sync/index.ts" \
    | cut -d' ' -f1
)"
if [[ "$actual_sync_hash" != "$expected_sync_hash" ]]; then
  echo "The deployed Loadrite sync baseline hash does not match." >&2
  exit 1
fi

"${compose_base[@]}" \
  -f "$repo_root/migration/supabase/docker-compose.ticket-printer-functions.yml" \
  up -d --force-recreate functions

for attempt in {1..30}; do
  status="$(
    curl -sS -o /dev/null -w '%{http_code}' \
      -H "apikey: $anon_key" \
      -H "Authorization: Bearer $anon_key" \
      "$base_url/functions/v1/address-autocomplete" 2>/dev/null || true
  )"
  [[ "$status" == "500" ]] && break
  sleep 1
done

request_status() {
  curl -sS -o "$response_file" -w '%{http_code}' \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
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
  echo "PASS: $function_name secret-free guard"
}

assert_json_error \
  address-autocomplete 500 "not configured" \
  -X POST -H "Content-Type: application/json" -d '{"input":"N"}'
assert_json_error loadrite 500 "not configured"
assert_json_error \
  loadrite-sync 500 "Missing one or more required secrets" \
  -X POST -H "Content-Type: application/json" -d '{}'
for function_name in \
  send-ticket-email \
  send-report-email \
  send-order-delivered-email; do
  assert_json_error \
    "$function_name" 500 "not configured" \
    -X POST -H "Content-Type: application/json" -d '{}'
done

for function_name in \
  agent-action \
  agent-container-restart \
  agent-containers \
  agent-logs-stream \
  agent-metrics \
  agent-status \
  delete-account; do
  status="$(
    curl -sS -o "$response_file" -w '%{http_code}' \
      -H "apikey: $anon_key" \
      "$base_url/functions/v1/$function_name"
  )"
  if [[ "$status" != "401" ]]; then
    echo "$function_name expected HTTP 401, received $status" >&2
    cat "$response_file" >&2
    exit 1
  fi
  echo "PASS: $function_name rejects missing authentication"
done

if docker logs "$functions_container" --since 5m 2>&1 \
    | grep -Eiq 'panic|segmentation fault|worker boot error'; then
  echo "The Edge Runtime logged a worker failure." >&2
  exit 1
fi

echo "Ticket Printer secret-free Edge Function acceptance passed."
