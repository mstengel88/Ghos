#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${SUPABASE_LAB_ENV_FILE:-$repo_root/migration/supabase/runtime/stack/.env}"
base_url="${SUPABASE_LAB_URL:-http://127.0.0.1:8000}"
functions_container="${SUPABASE_FUNCTIONS_CONTAINER:-supabase-edge-functions}"

if [[ "$base_url" != "http://localhost:"* \
      && "$base_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing Edge Function test for non-local Supabase URL: $base_url" >&2
  exit 1
fi

if [[ "$functions_container" != "supabase-edge-functions" ]]; then
  echo "Refusing Edge Function test for unexpected container: $functions_container" >&2
  exit 1
fi

if [[ ! -f "$env_file" ]]; then
  echo "Supabase lab environment file not found: $env_file" >&2
  exit 1
fi

for command_name in curl jq docker sha256sum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command is unavailable: $command_name" >&2
    exit 1
  fi
done

anon_key="$(grep '^ANON_KEY=' "$env_file" | cut -d= -f2-)"
if [[ -z "$anon_key" ]]; then
  echo "The local anonymous key is missing." >&2
  exit 1
fi

if ! docker ps --format '{{.Names}}' | grep -qx "$functions_container"; then
  echo "The local Edge Functions container is not running." >&2
  exit 1
fi

expected_carrier_hash="5932eeaf6d969561b5279812d44d8c7c137a50080d6e1dfe2db7fd495c5f2354"
expected_shopify_hash="e8fde587e01520d9c87a6dafec30baa5f3b3730d1b43a5d196fcb68c7d940aee"
expected_calc_hash="2d9384f4d5219b32515aa274986b8db04dc8665eaf7626eedb262f21ed68e407"

assert_hash() {
  local label="$1"
  local expected="$2"
  local path="$3"
  local actual
  actual="$(sha256sum "$path" | cut -d' ' -f1)"
  if [[ "$actual" != "$expected" ]]; then
    echo "$label source hash mismatch: expected $expected, received $actual" >&2
    exit 1
  fi
  echo "PASS: $label source matches the deployed capture"
}

assert_hash \
  "carrier-service" \
  "$expected_carrier_hash" \
  "$repo_root/migration/supabase/baselines/local-delivery/functions/carrier-service/index.ts"
assert_hash \
  "shopify-api" \
  "$expected_shopify_hash" \
  "$repo_root/migration/supabase/baselines/local-delivery/functions/shopify-api/index.ts"
assert_hash \
  "shipping calculator" \
  "$expected_calc_hash" \
  "$repo_root/migration/supabase/baselines/local-delivery/functions/shopify-api/shipping-calc.ts"

response_file="$(mktemp)"
headers_file="$(mktemp)"
cleanup() {
  rm -f "$response_file" "$headers_file"
}
trap cleanup EXIT

request_status() {
  curl -sS -o "$response_file" -D "$headers_file" -w '%{http_code}' \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    "$@"
}

assert_status() {
  local label="$1"
  local expected="$2"
  local actual="$3"
  if [[ "$actual" != "$expected" ]]; then
    echo "$label failed: expected HTTP $expected, received $actual" >&2
    cat "$response_file" >&2
    exit 1
  fi
  echo "PASS: $label"
}

status="$(request_status \
  -X OPTIONS \
  "$base_url/functions/v1/carrier-service")"
assert_status "carrier-service CORS preflight" "200" "$status"
grep -qi '^access-control-allow-origin: \*' "$headers_file"
echo "PASS: carrier-service CORS header"

status="$(request_status \
  "$base_url/functions/v1/carrier-service")"
assert_status "carrier-service non-POST fallback" "200" "$status"
jq -e '.rates == []' "$response_file" >/dev/null
echo "PASS: carrier-service non-POST response contract"

status="$(request_status \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{}' \
  "$base_url/functions/v1/carrier-service")"
assert_status "carrier-service missing-rate fallback" "200" "$status"
jq -e '.rates == []' "$response_file" >/dev/null
echo "PASS: carrier-service missing-rate response contract"

status="$(request_status \
  "$base_url/functions/v1/shopify-api?action=products")"
assert_status "shopify-api secret-free configuration guard" "500" "$status"
jq -e '.error == "SHOPIFY_STORE_DOMAIN not configured"' \
  "$response_file" >/dev/null
echo "PASS: shopify-api does not attempt an external call without configuration"

if docker logs "$functions_container" --since 5m 2>&1 \
    | grep -Eiq 'panic|segmentation fault|worker boot error'; then
  echo "The Edge Functions container logged a runtime failure." >&2
  exit 1
fi

echo "Local-Delivery secret-free Edge Function acceptance passed."
