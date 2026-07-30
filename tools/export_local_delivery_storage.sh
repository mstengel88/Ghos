#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
env_file="${LOCAL_DELIVERY_STORAGE_ENV_FILE:-$repo_root/migration/supabase/secrets/local-delivery-storage.env}"
output_dir="${LOCAL_DELIVERY_STORAGE_OUTPUT:-$repo_root/migration/supabase/exports/storage/local-delivery/initial}"
expected_url="https://mtntrlbuhcbdrngiubdu.supabase.co"

if [[ ! -f "$env_file" ]]; then
  printf 'Private Local Delivery Storage environment file not found: %s\n' \
    "$env_file" >&2
  printf '%s\n' \
    'Create it with SUPABASE_URL and SUPABASE_SERVICE_ROLE_KEY, then set mode 0600.' \
    'The migration/supabase/secrets directory is ignored by Git.' >&2
  exit 1
fi

file_mode="$(stat -f '%Lp' "$env_file" 2>/dev/null || stat -c '%a' "$env_file")"
if [[ "$file_mode" != "600" ]]; then
  printf 'Refusing credential file with mode %s; expected 600: %s\n' \
    "$file_mode" "$env_file" >&2
  exit 1
fi

configured_url="$(
  awk -F= '
    $1 == "SUPABASE_URL" {
      value = substr($0, index($0, "=") + 1)
      gsub(/^["'\'']|["'\'']$/, "", value)
      print value
      exit
    }
  ' "$env_file"
)"
if [[ "${configured_url%/}" != "$expected_url" ]]; then
  printf 'Refusing unexpected Local Delivery Supabase URL in %s.\n' \
    "$env_file" >&2
  exit 1
fi

service_key="$(
  awk -F= '
    $1 == "SUPABASE_SERVICE_ROLE_KEY" {
      value = substr($0, index($0, "=") + 1)
      gsub(/^["'\'']|["'\'']$/, "", value)
      print value
      exit
    }
  ' "$env_file"
)"
if [[ "${#service_key}" -lt 20 ]]; then
  printf 'SUPABASE_SERVICE_ROLE_KEY is missing or invalid in %s.\n' \
    "$env_file" >&2
  exit 1
fi

if [[ "$service_key" == sb_secret_* ]]; then
  :
elif [[ "$service_key" == *.*.* ]]; then
  jwt_payload="${service_key#*.}"
  jwt_payload="${jwt_payload%%.*}"
  while (( ${#jwt_payload} % 4 != 0 )); do
    jwt_payload="${jwt_payload}="
  done
  jwt_payload="${jwt_payload//_//}"
  jwt_payload="${jwt_payload//-/+}"
  if base64 --help >/dev/null 2>&1; then
    key_role="$(
      printf '%s' "$jwt_payload" |
        base64 --decode 2>/dev/null |
        jq -r '.role // empty'
    )"
  else
    key_role="$(
      printf '%s' "$jwt_payload" |
        base64 -D 2>/dev/null |
        jq -r '.role // empty'
    )"
  fi
  if [[ "$key_role" != "service_role" ]]; then
    printf '%s\n' \
      'Refusing a legacy Supabase key whose JWT role is not service_role.' \
      'Use the project service_role key or a modern sb_secret_ key, not the anon/publishable key.' >&2
    exit 1
  fi
else
  printf '%s\n' \
    'The configured key is neither a legacy service_role JWT nor a modern sb_secret_ key.' >&2
  exit 1
fi
unset service_key

python3 "$repo_root/tools/export_supabase_storage.py" \
  --env-file "$env_file" \
  --bucket dispatch-photos \
  --output "$output_dir" \
  --expected-count "${LOCAL_DELIVERY_STORAGE_EXPECTED_COUNT:-470}"
