#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
functions_env="$script_dir/functions.env"

if [[ ! -r "$functions_env" ]]; then
  printf 'Integration configuration is missing: %s\n' "$functions_env" >&2
  exit 1
fi

read_value() {
  local name="$1"
  sed -n "s/^${name}=//p" "$functions_env" | tail -n 1
}

required=(
  GOOGLE_MAPS_API_KEY
  RESEND_API_KEY
  LOADRITE_API_TOKEN
  LOADRITE_SYNC_USER_ID
  DUMP_SITE_QR_TOKEN
  DUMP_SITE_BRIDGE_SECRET
  SHOPIFY_API_KEY
  SHOPIFY_API_SECRET
)

missing=()
for name in "${required[@]}"; do
  value="$(read_value "$name")"
  [[ -n "$value" ]] || missing+=("$name")
done

if [[ "$(read_value MODERN_RETAIL_ENABLED)" == "true" ]]; then
  for name in \
    MODERN_RETAIL_API_USERNAME \
    MODERN_RETAIL_API_PASSWORD \
    MODERN_RETAIL_DUMP_ITEM_MAP; do
    value="$(read_value "$name")"
    [[ -n "$value" ]] || missing+=("$name")
  done
fi

if ((${#missing[@]} > 0)); then
  printf 'Operations cutover is blocked. Missing integration settings:\n' >&2
  printf '  - %s\n' "${missing[@]}" >&2
  exit 1
fi

mode="$(stat -c '%a' "$functions_env" 2>/dev/null || stat -f '%Lp' "$functions_env")"
if [[ "$mode" != "600" ]]; then
  printf 'Integration configuration must have mode 600 (currently %s).\n' "$mode" >&2
  exit 1
fi

printf 'Operations integration configuration passed required-setting and permission checks.\n'
