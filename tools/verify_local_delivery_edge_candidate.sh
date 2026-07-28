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
mock_response_file="$(mktemp)"
mock_pid=""
test_origin_ids=(
  "00000000-0000-4000-8000-0000000000a1"
  "00000000-0000-4000-8000-0000000000b2"
  "00000000-0000-4000-8000-0000000000d0"
)

cleanup() {
  if [[ -n "${service_role_key:-}" ]]; then
    curl -sS \
      -X DELETE \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$base_url/rest/v1/origin_addresses?id=in.($(IFS=,; echo "${test_origin_ids[*]}"))" \
      >/dev/null 2>&1 || true
  fi
  if [[ -n "$mock_pid" ]]; then
    kill "$mock_pid" 2>/dev/null || true
    wait "$mock_pid" 2>/dev/null || true
  fi
  rm -f "$mock_log" "$response_file" "$mock_response_file"
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
service_role_key="$(grep '^SERVICE_ROLE_KEY=' "$env_file" | cut -d= -f2-)"
if [[ -z "$service_role_key" ]]; then
  echo "The local service-role key is missing." >&2
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
  if curl -fsS "$mock_url/health" >/dev/null 2>&1; then
    break
  fi
  sleep 0.25
done

if ! curl -fsS "$mock_url/health" >/dev/null 2>&1; then
  echo "The local external-service mock did not start." >&2
  cat "$mock_log" >&2
  exit 1
fi

echo "PASS: localhost-only external-service mock is ready"

curl -fsS \
  -X POST \
  -H "apikey: $service_role_key" \
  -H "Authorization: Bearer $service_role_key" \
  -H "Content-Type: application/json" \
  -H "Prefer: resolution=merge-duplicates,return=minimal" \
  -d '[
    {
      "id": "00000000-0000-4000-8000-0000000000a1",
      "label": "Vendor A",
      "address": "Vendor A Origin",
      "is_active": false
    },
    {
      "id": "00000000-0000-4000-8000-0000000000b2",
      "label": "Vendor B",
      "address": "Vendor B Origin",
      "is_active": false
    },
    {
      "id": "00000000-0000-4000-8000-0000000000d0",
      "label": "Local Candidate Default",
      "address": "Default Test Origin",
      "is_active": true
    }
  ]' \
  "$base_url/rest/v1/origin_addresses?on_conflict=id"

echo "PASS: disposable vendor and default origins are staged"

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

curl -fsS -X POST "$mock_url/reset" >/dev/null

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
        "items": [
          { "variant_id": 111, "quantity": 45 },
          { "variant_id": 222, "quantity": 1 }
        ],
        "currency": "USD"
      }
    }' \
    "$base_url/functions/v1/carrier-service"
)"

if [[ "$status" != "200" ]]; then
  echo "Multi-origin carrier callback failed: expected HTTP 200, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi

jq -e '
  .rates
  | length == 1
    and .[0].total_price == "29120"
    and .[0].description == "Delivery (4 loads required)"
' "$response_file" >/dev/null

curl -fsS "$mock_url/requests" >"$mock_response_file"
jq -e '
  [.requests[]
    | select(.pathname == "/maps")
    | .search]
  | length == 2
    and any(.[]; contains("Vendor+A+Origin"))
    and any(.[]; contains("Vendor+B+Origin"))
' "$mock_response_file" >/dev/null

echo "PASS: vendor origins, route caching, and four-load pricing return \$291.20"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    -H "Content-Type: application/json" \
    -d '{
      "rate": {
        "destination": {
          "address1": "Beyond Limit",
          "city": "Remote",
          "province": "WI",
          "postal_code": "00000",
          "country": "US"
        },
        "items": [
          { "variant_id": 111, "quantity": 1 }
        ],
        "currency": "USD"
      }
    }' \
    "$base_url/functions/v1/carrier-service"
)"

if [[ "$status" != "200" ]]; then
  echo "Mileage-limit callback failed: expected HTTP 200, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi
jq -e '.rates == []' "$response_file" >/dev/null
echo "PASS: routes beyond the 50-mile limit return no carrier rate"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    "$base_url/functions/v1/shopify-api?action=product&product_id=123"
)"
if [[ "$status" != "200" ]]; then
  echo "Mocked Shopify product request failed: expected HTTP 200, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi
jq -e '
  .product.id == 123
    and .product.title == "Local Mock Product"
    and .product.vendor == "Vendor A"
    and .product.variants[0].id == 111
    and .product.variants[0].sku == "LOCAL-111"
' "$response_file" >/dev/null
echo "PASS: Shopify token exchange and product transformation succeed"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    -H "Content-Type: application/json" \
    -d '{
      "product_id": 123,
      "variant_id": 111,
      "quantity": 2,
      "distance_miles": 10,
      "truck_type": "pickup"
    }' \
    "$base_url/functions/v1/shopify-api?action=shipping_quote"
)"
if [[ "$status" != "200" ]]; then
  echo "Mocked Shopify shipping quote failed: expected HTTP 200, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi
jq -e '
  .weight_lbs == 2000
    and .truck.id == "pickup"
    and .zone == "Local (0-50 mi)"
    and .baseCost == 150
    and .fuelSurcharge == 22.5
    and .total == 172.5
' "$response_file" >/dev/null
echo "PASS: Shopify shipping quote calculation returns the expected \$172.50"

status="$(
  curl -sS -o "$response_file" -w '%{http_code}' \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $anon_key" \
    "$base_url/functions/v1/shopify-api?action=product&product_id=999999"
)"
if [[ "$status" != "500" ]]; then
  echo "Mocked Shopify GraphQL error failed: expected HTTP 500, received $status" >&2
  cat "$response_file" >&2
  exit 1
fi
jq -e '.error | contains("Synthetic Shopify GraphQL failure")' \
  "$response_file" >/dev/null
echo "PASS: Shopify GraphQL failures return the expected safe error contract"

echo "Reviewed Local-Delivery Edge Function candidate acceptance passed."
