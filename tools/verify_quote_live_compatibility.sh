#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
runtime_env="$repo_root/migration/supabase/runtime/stack/.env"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
api_base="${SUPABASE_LOCAL_API_URL:-http://127.0.0.1:8000}"
timestamp="$(date -u +%Y%m%d%H%M%S)"
fixture_tag="ghos-quote-compat-${timestamp}"
fixture_shop="${fixture_tag}.myshopify.invalid"
fixture_sku="GHOS-Q-${timestamp}"
fixture_prefix="${timestamp: -6}"
fixture_company="ghos-quote-company-${timestamp}"
service_role_key=""
anon_key=""
quote_id=""
origin_id=""

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

api_request() {
  local key="$1"
  shift
  curl -fsS \
    -H "apikey: $key" \
    -H "Authorization: Bearer $key" \
    "$@"
}

cleanup() {
  set +e
  if [[ -n "$service_role_key" ]]; then
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/custom_delivery_quotes?id=eq.$quote_id" \
      >/dev/null 2>&1
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/product_source_map?sku=eq.$fixture_sku" \
      >/dev/null 2>&1
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/shipping_material_rules?prefix=eq.$fixture_prefix" \
      >/dev/null 2>&1
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/origin_addresses?label=eq.$fixture_tag" \
      >/dev/null 2>&1
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/dispatch_b2b_companies?id=eq.$fixture_company" \
      >/dev/null 2>&1
    api_request "$service_role_key" \
      -X DELETE \
      "$api_base/rest/v1/shopify_app_settings?shop=eq.$fixture_shop" \
      >/dev/null 2>&1
  fi
  unset service_role_key anon_key
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
  printf 'Refusing Quote Live compatibility test for non-local URL: %s\n' \
    "$api_base" >&2
  exit 1
fi
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi
if [[ ! -s "$runtime_env" ]]; then
  printf 'Local Supabase runtime environment is missing: %s\n' \
    "$runtime_env" >&2
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

service_role_key="$(read_env_value SERVICE_ROLE_KEY)"
anon_key="$(read_env_value ANON_KEY)"
if [[ -z "$service_role_key" || -z "$anon_key" ]]; then
  printf '%s\n' 'The local API keys are empty.' >&2
  exit 1
fi

printf '%s\n' 'Verifying the consolidated quote schema contract...'
contract="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres -Atqc \
    "
      with required(table_name, column_name) as (
        values
          ('custom_delivery_quotes', 'quote_total_cents'),
          ('custom_delivery_quotes', 'source_breakdown'),
          ('custom_delivery_quotes', 'line_items'),
          ('custom_delivery_quotes', 'company_name'),
          ('custom_delivery_quotes', 'shopify_company_id'),
          ('custom_delivery_quotes', 'payment_terms_due_in_days'),
          ('custom_delivery_quotes', 'tax_exempt'),
          ('custom_delivery_quotes', 'created_by_user_id'),
          ('dispatch_b2b_companies', 'contractor_tier'),
          ('dispatch_b2b_companies', 'payment_terms_due_in_days'),
          ('dispatch_b2b_companies', 'tax_exempt'),
          ('product_source_map', 'contractor_tier_1_price'),
          ('product_source_map', 'contractor_tier_2_price'),
          ('product_source_map', 'unit_label'),
          ('shipping_material_rules', 'truck_capacity'),
          ('shipping_material_rules', 'vendor_source'),
          ('origin_addresses', 'is_active'),
          ('shopify_app_settings', 'enable_calculated_rates')
      )
      select
        (select count(*) from required),
        (
          select count(*)
          from required r
          join information_schema.columns c
            on c.table_schema = 'public'
           and c.table_name = r.table_name
           and c.column_name = r.column_name
        ),
        (
          select count(*)
          from pg_class c
          join pg_namespace n on n.oid = c.relnamespace
          where n.nspname = 'public'
            and c.relname in (
              'custom_delivery_quotes',
              'dispatch_b2b_companies',
              'product_source_map',
              'shipping_material_rules',
              'origin_addresses',
              'shopify_app_settings'
            )
            and c.relrowsecurity
        );
    "
)"
if [[ "$contract" != "18|18|6" ]]; then
  printf 'Unexpected consolidated quote schema contract: %s\n' \
    "$contract" >&2
  exit 1
fi

printf '%s\n' 'Waiting for the local Data API...'
for _ in $(seq 1 45); do
  if api_request "$service_role_key" \
      "$api_base/rest/v1/custom_delivery_quotes?select=id&limit=1" \
      >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
api_request "$service_role_key" \
  "$api_base/rest/v1/custom_delivery_quotes?select=id&limit=1" \
  >/dev/null

printf '%s\n' 'Verifying anonymous isolation for server-managed quote data...'
for endpoint in \
  custom_delivery_quotes \
  dispatch_b2b_companies \
  product_source_map \
  shipping_material_rules \
  shopify_app_settings; do
  response="$(
    api_request "$anon_key" \
      "$api_base/rest/v1/$endpoint?select=*&limit=1"
  )"
  if [[ "$(jq 'length' <<<"$response")" != "0" ]]; then
    printf 'Anonymous role could read %s.\n' "$endpoint" >&2
    exit 1
  fi
done

printf '%s\n' 'Creating service-role quote dependencies through PostgREST...'
origin_response="$(
  jq -n \
    --arg label "$fixture_tag" \
    '{
      label: $label,
      address: "W185 N7487 Narrow Ln, Menomonee Falls, WI 53051",
      is_active: true
    }' |
    api_request "$service_role_key" \
      -X POST \
      -H 'Content-Type: application/json' \
      -H 'Prefer: return=representation' \
      --data-binary @- \
      "$api_base/rest/v1/origin_addresses?select=id,label"
)"
origin_id="$(jq -er '.[0].id' <<<"$origin_response")"

api_request "$service_role_key" \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'Prefer: return=minimal' \
  --data-binary "$(
    jq -n \
      --arg prefix "$fixture_prefix" \
      --arg vendor "$fixture_tag" \
      '{
        prefix: $prefix,
        material_name: "Quote compatibility material",
        truck_capacity: 22,
        is_active: true,
        sort_order: 9999,
        vendor_source: $vendor
      }'
  )" \
  "$api_base/rest/v1/shipping_material_rules" >/dev/null

product_response="$(
  jq -n \
    --arg sku "$fixture_sku" \
    --arg vendor "$fixture_tag" \
    '{
      sku: $sku,
      product_title: "Quote compatibility product",
      pickup_vendor: $vendor,
      price: 42.50,
      contractor_tier_1_price: 39.50,
      contractor_tier_2_price: 36.50,
      unit_label: "Ton",
      variant_id: "gid://shopify/ProductVariant/0"
    }' |
    api_request "$service_role_key" \
      -X POST \
      -H 'Content-Type: application/json' \
      -H 'Prefer: return=representation' \
      --data-binary @- \
      "$api_base/rest/v1/product_source_map?select=id,sku,price,contractor_tier_1_price,unit_label"
)"
jq -er '.[0].id' <<<"$product_response" >/dev/null

api_request "$service_role_key" \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'Prefer: return=minimal' \
  --data-binary "$(
    jq -n \
      --arg id "$fixture_company" \
      '{
        id: $id,
        shopify_company_id: "gid://shopify/Company/0",
        company_name: "Quote compatibility company",
        contractor_tier: "tier1",
        tax_exempt: false,
        payment_terms_name: "Net 30",
        payment_terms_due_in_days: 30
      }'
  )" \
  "$api_base/rest/v1/dispatch_b2b_companies" >/dev/null

api_request "$service_role_key" \
  -X POST \
  -H 'Content-Type: application/json' \
  -H 'Prefer: return=minimal' \
  --data-binary "$(
    jq -n \
      --arg shop "$fixture_shop" \
      '{
        shop: $shop,
        enable_calculated_rates: true,
        enable_remote_surcharge: true,
        show_vendor_source: true
      }'
  )" \
  "$api_base/rest/v1/shopify_app_settings" >/dev/null

printf '%s\n' 'Testing quote create, read, update, and delete compatibility...'
quote_response="$(
  jq -n \
    --arg shop "$fixture_shop" \
    --arg sku "$fixture_sku" \
    '{
      shop: $shop,
      customer_name: "Quote Compatibility Customer",
      company_name: "Quote compatibility company",
      address1: "N88 W14181 Main St",
      city: "Menomonee Falls",
      province: "WI",
      postal_code: "53051",
      country: "US",
      quote_total_cents: 10750,
      service_name: "Aggregate Delivery",
      description: "Local-only migration acceptance fixture",
      eta: "2-3 business days",
      summary: "One product and one delivery",
      source_breakdown: [{source: "Green Hills", cents: 4250}],
      line_items: [{sku: $sku, quantity: 1, price: 42.50}],
      payment_terms_name: "Due on receipt",
      payment_terms_due_in_days: 0,
      tax_exempt: false,
      billing_country: "US"
    }' |
    api_request "$service_role_key" \
      -X POST \
      -H 'Content-Type: application/json' \
      -H 'Prefer: return=representation' \
      --data-binary @- \
      "$api_base/rest/v1/custom_delivery_quotes?select=id,quote_total_cents,line_items,company_name"
)"
quote_id="$(jq -er '.[0].id' <<<"$quote_response")"
if [[ "$(jq -er '.[0].quote_total_cents' <<<"$quote_response")" != "10750" \
      || "$(jq -er '.[0].line_items[0].sku' <<<"$quote_response")" \
        != "$fixture_sku" ]]; then
  printf 'Unexpected inserted quote response: %s\n' "$quote_response" >&2
  exit 1
fi

api_request "$service_role_key" \
  -X PATCH \
  -H 'Content-Type: application/json' \
  -H 'Prefer: return=minimal' \
  --data-binary '{"quote_total_cents":11250,"summary":"Updated quote"}' \
  "$api_base/rest/v1/custom_delivery_quotes?id=eq.$quote_id" >/dev/null

updated_quote="$(
  api_request "$service_role_key" \
    "$api_base/rest/v1/custom_delivery_quotes?id=eq.$quote_id&select=id,quote_total_cents,summary"
)"
if [[ "$(jq -er '.[0].quote_total_cents' <<<"$updated_quote")" != "11250" \
      || "$(jq -er '.[0].summary' <<<"$updated_quote")" \
        != "Updated quote" ]]; then
  printf 'Unexpected updated quote response: %s\n' "$updated_quote" >&2
  exit 1
fi

anon_quote="$(
  api_request "$anon_key" \
    "$api_base/rest/v1/custom_delivery_quotes?id=eq.$quote_id&select=id"
)"
if [[ "$(jq 'length' <<<"$anon_quote")" != "0" ]]; then
  printf '%s\n' 'Anonymous role could read the quote fixture.' >&2
  exit 1
fi

active_origin="$(
  api_request "$anon_key" \
    "$api_base/rest/v1/origin_addresses?id=eq.$origin_id&is_active=eq.true&select=id,label"
)"
if [[ "$(jq -er '.[0].label' <<<"$active_origin")" != "$fixture_tag" ]]; then
  printf '%s\n' 'The public active-origin contract did not match.' >&2
  exit 1
fi

cleanup
trap - EXIT

leftovers="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres -Atqc \
    "
      select
        (select count(*) from public.custom_delivery_quotes
          where shop = '$fixture_shop')
        + (select count(*) from public.product_source_map
          where sku = '$fixture_sku')
        + (select count(*) from public.shipping_material_rules
          where prefix = '$fixture_prefix')
        + (select count(*) from public.origin_addresses
          where label = '$fixture_tag')
        + (select count(*) from public.dispatch_b2b_companies
          where id = '$fixture_company')
        + (select count(*) from public.shopify_app_settings
          where shop = '$fixture_shop');
    "
)"
if [[ "$leftovers" != "0" ]]; then
  printf 'Quote compatibility test left %s fixture row(s).\n' \
    "$leftovers" >&2
  exit 1
fi

printf '%s\n' \
  'Quote Live compatibility passed against the local Local-Delivery target.'
