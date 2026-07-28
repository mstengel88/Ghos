#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
stack_dir="$repo_root/migration/supabase/runtime/stack"
env_file="${SUPABASE_LAB_ENV_FILE:-$stack_dir/.env}"
base_url="${SUPABASE_LAB_URL:-http://127.0.0.1:8000}"
functions_container="${SUPABASE_FUNCTIONS_CONTAINER:-supabase-edge-functions}"
response_file="$(mktemp)"
bridge_secret="local-compatibility-bridge-secret"
mounted_functions_dir="$stack_dir/volumes/functions"

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

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
    create --force-recreate functions >/dev/null 2>&1 || true
  docker start "$functions_container" >/dev/null 2>&1 || true
  rm -f "$response_file"
  rm -rf \
    "$mounted_functions_dir/dump-site-api" \
    "$mounted_functions_dir/dump-site-bridge"
}
trap restore_local_delivery EXIT

anon_key="$(grep '^ANON_KEY=' "$env_file" | cut -d= -f2-)"
if [[ -z "$anon_key" ]]; then
  echo "The local anonymous key is missing." >&2
  exit 1
fi

expected_api_hash="4732a4a6e92a7bcfcb200a66d657d7fd2478f8892eafa1ee7c55f9057761d38b"
expected_bridge_hash="3bdf9a9b88cd195a0113d93f5c7e337bc3f4d25210139e87f05cb13dcdfcecf3"
source_root="${DUMP_SITE_SOURCE_ROOT:-/Users/mattstengel/Documents/GreenHills APP}"

actual_api_hash="$(sha256_file "$source_root/supabase/functions/dump-site-api/index.ts")"
actual_bridge_hash="$(sha256_file "$source_root/supabase/functions/dump-site-bridge/index.ts")"
if [[ "$actual_api_hash" != "$expected_api_hash" ]]; then
  echo "The Dump Site API source hash no longer matches the inventoried deployment." >&2
  exit 1
fi
if [[ "$actual_bridge_hash" != "$expected_bridge_hash" ]]; then
  echo "The Dump Site bridge source hash no longer matches the inventoried deployment." >&2
  exit 1
fi

if [[ -e "$mounted_functions_dir/dump-site-api" \
      || -e "$mounted_functions_dir/dump-site-bridge" ]]; then
  echo "Refusing to replace an existing Dump Site function mount." >&2
  exit 1
fi
cp -R \
  "$source_root/supabase/functions/dump-site-api" \
  "$source_root/supabase/functions/dump-site-bridge" \
  "$mounted_functions_dir/"

"${compose_base[@]}" \
  -f "$repo_root/migration/supabase/docker-compose.dump-site-functions.yml" \
  create --force-recreate functions
docker start "$functions_container" >/dev/null

for attempt in {1..30}; do
  status="$(
    curl -sS -o /dev/null -w '%{http_code}' \
      --connect-timeout 2 --max-time 5 \
      -H "apikey: $anon_key" \
      "$base_url/functions/v1/dump-site-api" 2>/dev/null || true
  )"
  [[ "$status" == "405" ]] && break
  sleep 1
done

request_status() {
  curl -sS -o "$response_file" -w '%{http_code}' \
    --connect-timeout 2 --max-time 10 \
    -H "apikey: $anon_key" \
    "$@"
}

assert_json() {
  local label="$1"
  local expected_status="$2"
  local jq_filter="$3"
  shift 3
  local status
  status="$(request_status "$@")"
  if [[ "$status" != "$expected_status" ]]; then
    echo "$label expected HTTP $expected_status, received $status" >&2
    cat "$response_file" >&2
    docker logs "$functions_container" --tail 120 >&2 || true
    exit 1
  fi
  if ! jq -e "$jq_filter" "$response_file" >/dev/null; then
    echo "$label returned an unexpected payload:" >&2
    cat "$response_file" >&2
    exit 1
  fi
  echo "PASS: $label"
}

assert_json \
  "Dump Site API rejects non-POST requests" 405 \
  '.error == "POST requests only."' \
  "$base_url/functions/v1/dump-site-api"

assert_json \
  "Dump Site API rejects unknown routes" 404 \
  '.error == "Dump-site route not found."' \
  -X POST -H "Content-Type: application/json" -d '{}' \
  "$base_url/functions/v1/dump-site-api/not-a-route"

assert_json \
  "Dump Site API validates submission fields before database access" 400 \
  '.error == "Truck number, driver name, material, and vehicle are required."' \
  -X POST -H "Content-Type: application/json" -d '{}' \
  "$base_url/functions/v1/dump-site-api/submit"

assert_json \
  "Dump Site API rejects an invalid QR token" 200 \
  '.allowed == false' \
  -X POST -H "Content-Type: application/json" -d '{"qrToken":"wrong"}' \
  "$base_url/functions/v1/dump-site-api/qr-access"

assert_json \
  "Dump Site API accepts the local compatibility QR token" 200 \
  '.allowed == true' \
  -X POST -H "Content-Type: application/json" \
  -d '{"qrToken":"local-compatibility-qr-token"}' \
  "$base_url/functions/v1/dump-site-api/qr-access"

assert_json \
  "Dump Site bridge rejects missing authorization" 401 \
  '.error == "Bridge authorization failed."' \
  -X POST -H "Content-Type: application/json" -d '{"bridgeId":"local-test"}' \
  "$base_url/functions/v1/dump-site-bridge/health"

assert_json \
  "Dump Site bridge rejects the wrong shared secret" 401 \
  '.error == "Bridge authorization failed."' \
  -X POST -H "Authorization: Bearer wrong" \
  -H "Content-Type: application/json" -d '{"bridgeId":"local-test"}' \
  "$base_url/functions/v1/dump-site-bridge/health"

assert_json \
  "Dump Site bridge validates bridge ID after authentication" 400 \
  '.error == "A bridge ID is required."' \
  -X POST -H "Authorization: Bearer local-compatibility-bridge-secret" \
  -H "Content-Type: application/json" -d '{}' \
  "$base_url/functions/v1/dump-site-bridge/health"

assert_json \
  "Dump Site bridge health accepts the local compatibility secret" 200 \
  '.ok == true and .bridgeId == "local-test"' \
  -X POST -H "Authorization: Bearer $bridge_secret" \
  -H "Content-Type: application/json" -d '{"bridgeId":"local-test"}' \
  "$base_url/functions/v1/dump-site-bridge/health"

if docker logs "$functions_container" --since 5m 2>&1 \
    | grep -Eiq 'panic|segmentation fault|worker boot error'; then
  echo "The Edge Runtime logged a worker failure." >&2
  exit 1
fi

echo "Dump Site local Edge Function acceptance passed."
