#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${SUPABASE_LAB_ENV_FILE:-$repo_root/migration/supabase/runtime/stack/.env}"
base_url="${SUPABASE_LAB_URL:-http://localhost:8000}"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"

if [[ "$base_url" != "http://localhost:"* \
      && "$base_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing Auth acceptance test for non-local URL: $base_url" >&2
  exit 1
fi

if [[ "$db_container" != "supabase-db" ]]; then
  echo "Refusing Auth acceptance test for unexpected container: $db_container" >&2
  exit 1
fi

if [[ ! -f "$env_file" ]]; then
  echo "Supabase lab environment file not found: $env_file" >&2
  exit 1
fi

for command_name in curl jq docker; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required command is unavailable: $command_name" >&2
    exit 1
  fi
done

anon_key="$(grep '^ANON_KEY=' "$env_file" | cut -d= -f2-)"
service_role_key="$(grep '^SERVICE_ROLE_KEY=' "$env_file" | cut -d= -f2-)"

if [[ -z "$anon_key" || -z "$service_role_key" ]]; then
  echo "The local anonymous or service-role key is missing." >&2
  exit 1
fi

response_file="$(mktemp)"
user_id=""
test_email="ghos-auth-acceptance-$(date +%s)-$$@example.invalid"
old_password="GHOS-old-password-$(date +%s)-$$"
new_password="GHOS-new-password-$(date +%s)-$$"

cleanup() {
  if [[ -n "$user_id" ]]; then
    curl -sS -o /dev/null \
      -X DELETE \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$base_url/auth/v1/admin/users/$user_id" || true
  fi

  rm -f "$response_file"
}
trap cleanup EXIT

request_status() {
  curl -sS -o "$response_file" -w '%{http_code}' "$@"
}

assert_status() {
  local label="$1"
  local expected="$2"
  local actual="$3"

  if [[ "$actual" != "$expected" ]]; then
    echo "$label failed: expected HTTP $expected, received $actual" >&2
    jq -c '{code, error_code, msg, message}' "$response_file" 2>/dev/null \
      || true
    exit 1
  fi

  echo "PASS: $label"
}

status="$(
  request_status \
    -X POST \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$test_email" \
        --arg password "$old_password" \
        '{email:$email,password:$password,email_confirm:true}'
    )" \
    "$base_url/auth/v1/admin/users"
)"
assert_status "administrator user creation" "200" "$status"
user_id="$(jq -r '.id // empty' "$response_file")"

if [[ -z "$user_id" ]]; then
  echo "Auth create response did not contain a user ID." >&2
  exit 1
fi

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$test_email" \
        --arg password "$old_password" \
        '{email:$email,password:$password}'
    )" \
    "$base_url/auth/v1/token?grant_type=password"
)"
assert_status "password sign-in" "200" "$status"
access_token="$(jq -r '.access_token // empty' "$response_file")"

if [[ -z "$access_token" ]]; then
  echo "Auth sign-in response did not contain an access token." >&2
  exit 1
fi

status="$(
  request_status \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $access_token" \
    "$base_url/auth/v1/user"
)"
assert_status "authenticated profile access" "200" "$status"

status="$(
  request_status \
    -X PUT \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $access_token" \
    -H "Content-Type: application/json" \
    -d "$(jq -nc --arg password "$new_password" '{password:$password}')" \
    "$base_url/auth/v1/user"
)"
assert_status "authenticated password change" "200" "$status"

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$test_email" \
        --arg password "$old_password" \
        '{email:$email,password:$password}'
    )" \
    "$base_url/auth/v1/token?grant_type=password"
)"
assert_status "old password rejection" "400" "$status"

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$test_email" \
        --arg password "$new_password" \
        '{email:$email,password:$password}'
    )" \
    "$base_url/auth/v1/token?grant_type=password"
)"
assert_status "new password sign-in" "200" "$status"
access_token="$(jq -r '.access_token // empty' "$response_file")"
refresh_token="$(jq -r '.refresh_token // empty' "$response_file")"

if [[ -z "$access_token" || -z "$refresh_token" ]]; then
  echo "Auth sign-in response did not contain both session tokens." >&2
  exit 1
fi

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $access_token" \
    "$base_url/auth/v1/logout"
)"
assert_status "logout" "204" "$status"

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(jq -nc --arg refresh_token "$refresh_token" '{refresh_token:$refresh_token}')" \
    "$base_url/auth/v1/token?grant_type=refresh_token"
)"
assert_status "logged-out refresh-token rejection" "400" "$status"

status="$(
  request_status \
    -X DELETE \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    "$base_url/auth/v1/admin/users/$user_id"
)"
assert_status "administrator user deletion" "200" "$status"
user_id=""

remaining_user_count="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres -Atc \
    "select count(*) from auth.users where email = '$test_email';"
)"

if [[ "$remaining_user_count" != "0" ]]; then
  echo "Disposable Auth user remained after cleanup." >&2
  exit 1
fi

echo "Local-Delivery Auth session acceptance passed with complete cleanup."
