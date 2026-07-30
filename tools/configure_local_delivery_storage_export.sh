#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
source_env="${LOCAL_DELIVERY_SOURCE_ENV_FILE:-/Users/mattstengel/local-delivery/dispatch-v2-sandbox/.env}"
target_env="${LOCAL_DELIVERY_STORAGE_ENV_FILE:-$repo_root/migration/supabase/secrets/local-delivery-storage.env}"
expected_url="https://mtntrlbuhcbdrngiubdu.supabase.co"

if [[ ! -f "$source_env" ]]; then
  printf 'Local Delivery source environment file not found: %s\n' \
    "$source_env" >&2
  exit 1
fi

read_value() {
  local key="$1"
  awk -F= -v requested_key="$key" '
    $1 == requested_key {
      value = substr($0, index($0, "=") + 1)
      gsub(/^["'\'']|["'\'']$/, "", value)
      print value
      exit
    }
  ' "$source_env"
}

supabase_url="$(read_value SUPABASE_URL)"
service_key="$(read_value SUPABASE_SERVICE_ROLE_KEY)"

if [[ "${supabase_url%/}" != "$expected_url" ]]; then
  printf '%s\n' \
    'The source application is not configured for the expected Local Delivery project.' >&2
  exit 1
fi
if [[ "${#service_key}" -lt 20 ]]; then
  printf '%s\n' \
    'The source application does not contain a usable server-only Supabase credential.' >&2
  exit 1
fi

mkdir -p "$(dirname "$target_env")"
temporary_env="$(mktemp "$(dirname "$target_env")/.local-delivery-storage.XXXXXX")"
cleanup() {
  unset service_key
  if [[ -f "$temporary_env" ]]; then
    rm -f "$temporary_env"
  fi
}
trap cleanup EXIT

printf 'SUPABASE_URL=%s\nSUPABASE_SERVICE_ROLE_KEY=%s\n' \
  "$expected_url" "$service_key" > "$temporary_env"
chmod 600 "$temporary_env"
mv "$temporary_env" "$target_env"
chmod 600 "$target_env"
unset service_key

if ! git -C "$repo_root" check-ignore -q "$target_env"; then
  printf '%s\n' \
    'The private Storage credential file is not ignored by Git; stopping.' >&2
  exit 1
fi

printf '%s\n' \
  'Local Delivery Storage export credentials were copied into the ignored migration secret store.' \
  "Credential file: $target_env" \
  'No secret values were printed or added to Git.'
