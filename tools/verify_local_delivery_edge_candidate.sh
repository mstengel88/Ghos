#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${SUPABASE_LAB_ENV_FILE:-$repo_root/migration/supabase/runtime/stack/.env}"
base_url="${SUPABASE_LAB_URL:-http://127.0.0.1:8000}"
mock_url="${EDGE_FUNCTION_MOCK_URL:-http://127.0.0.1:18765}"
functions_container="${SUPABASE_FUNCTIONS_CONTAINER:-supabase-edge-functions}"
candidate_root="$repo_root/migration/supabase/candidates/local-delivery/functions"
mock_log="$(mktemp)"
response_file="$(mktemp)"
mock_pid=""

cleanup() {
  if [[ -n "$mock_pid" ]]; then
    kill "$mock_pid" 2>/dev/null || true
    wait "$mock_pid" 2>/dev/null || true
  fi
  rm -f "$mock_log" "$response_file"
}
trap cleanup EXIT

if [[ "$base_url" != "http://localhost:"* \
      && "$base_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing candidate test for non-local Supabase URL: $base_url" >&2
  exit 1
fi

if [[ "$mock_url" != "http://localhost:"* \
      && "$mock_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing candidate test for non-local mock URL: $mock_url" >&2
  exit 1
fi

if [[ "$functions_container" != "supabase-edge-functions" ]]; then
  echo "Refusing candidate test for unexpected container: $functions_container" >&2
  exit 1
fi

if [[ ! -f "$env_file" ]]; then
  echo "Supabase lab environment file not found: $env_file" >&2
  exit 1
fi

for command_name in curl docker jq node; do
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

mounts="$(docker inspect "$functions_container" \
  --format '{{range .Mounts}}{{println .Source "=>" .Destination}}{{end}}')"
if ! grep -Fq \
    "$candidate_root/carrier-service => /home/deno/functions/carrier-service" \
    <<<"$mounts"; then
  echo "The reviewed carrier-service candidate is not mounted." >&2
  exit 1
fi

echo "PASS: reviewed carrier-service candidate is mounted"

node --test \
  "$candidate_root/carrier-service/delivery-math.test.mjs"

node "$repo_root/tools/fixtures/local_delivery_edge_mock.mjs" \
  >"$mock_log" 2>&1 &
mock_pid="$!"

for _ in {1..20}; do
  if curl -fsS "$mock_url/health" >/dev/null; then
    break
  fi
  sleep 0.25
done

if ! curl -fsS "$mock_url/health" >/dev/null; then
  echo "The local external-service mock did not start." >&2
  cat "$mock_log" >&2
  exit 1
fi

echo "PASS: localhost-only external-service mock is ready"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    -H "Content-Type: application/json" \
    -d '{
      "rate": {
        "destination": {
          "address1": "123 Main St",
          "city": "Milwaukee",
          "province": "WI",
          "postal_code": "53202",
          "country": "US"
        },
        "items": [],
        "currency": "USD"
      }
    }' \
    "$base_url/functions/v1/carrier-service"
)"

if [[ "$status" != "200" ]]; then
  echo "Carrier callback failed: expected HTTP 200, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi

jq -e '
  .rates
  | length == 1
    and .[0].service_code == "ghs_delivery"
    and .[0].total_price == "6240"
    and .[0].currency == "USD"
' "$response_file" >/dev/null

echo "PASS: mocked 30-minute round trip returns the expected \$62.40 rate"
echo "Reviewed Local-Delivery Edge Function candidate acceptance passed."
