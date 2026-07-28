#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
env_file="${SUPABASE_LAB_ENV_FILE:-$repo_root/migration/supabase/runtime/stack/.env}"
base_url="${SUPABASE_LAB_URL:-http://localhost:8000}"
mailpit_url="${MAILPIT_LAB_URL:-http://127.0.0.1:8025}"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"

if [[ "$base_url" != "http://localhost:"* \
      && "$base_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing Auth email test for non-local Supabase URL: $base_url" >&2
  exit 1
fi

if [[ "$mailpit_url" != "http://localhost:"* \
      && "$mailpit_url" != "http://127.0.0.1:"* ]]; then
  echo "Refusing Auth email test for non-local Mailpit URL: $mailpit_url" >&2
  exit 1
fi

if [[ "$db_container" != "supabase-db" ]]; then
  echo "Refusing Auth email test for unexpected container: $db_container" >&2
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
headers_file="$(mktemp)"
declare -a fixture_user_ids=()
declare -a fixture_message_ids=()

cleanup() {
  local message_id
  local user_id

  for message_id in "${fixture_message_ids[@]}"; do
    [[ -z "$message_id" ]] && continue
    curl -sS -o /dev/null \
      -X DELETE \
      -H "Content-Type: application/json" \
      -d "$(jq -nc --arg id "$message_id" '{IDs:[$id]}')" \
      "$mailpit_url/api/v1/messages" || true
  done

  for user_id in "${fixture_user_ids[@]}"; do
    [[ -z "$user_id" ]] && continue
    curl -sS -o /dev/null \
      -X DELETE \
      -H "apikey: $service_role_key" \
      -H "Authorization: Bearer $service_role_key" \
      "$base_url/auth/v1/admin/users/$user_id" || true
  done

  rm -f "$response_file" "$headers_file"
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

wait_for_message() {
  local email="$1"
  local attempt
  local message_id

  for attempt in {1..20}; do
    message_id="$(
      curl -sS "$mailpit_url/api/v1/messages" \
        | jq -r --arg email "$email" \
          '.messages[]
           | select(any(.To[]?; .Address == $email))
           | .ID' \
        | head -1
    )"

    if [[ -n "$message_id" ]]; then
      printf '%s' "$message_id"
      return 0
    fi

    sleep 0.25
  done

  echo "No captured email arrived for the disposable recipient." >&2
  return 1
}

verification_link() {
  local message_id="$1"

  curl -sS "$mailpit_url/api/v1/message/$message_id" \
    | jq -r \
      '.Text
       | match("https?://[^[:space:]<>]+/auth/v1/verify[^[:space:]<>\\)]+")
       | .string'
}

verify_email_link() {
  local label="$1"
  local message_id="$2"
  local link
  local status
  local location

  link="$(verification_link "$message_id")"
  if [[ -z "$link" || "$link" != *"/auth/v1/verify"* ]]; then
    echo "$label did not contain a valid local Auth verification link." >&2
    exit 1
  fi

  status="$(
    curl -sS -D "$headers_file" -o /dev/null -w '%{http_code}' "$link"
  )"

  if [[ "$status" != "302" && "$status" != "303" ]]; then
    echo "$label verification failed: expected redirect, received $status" >&2
    exit 1
  fi

  location="$(
    awk 'tolower($1) == "location:" {$1=""; sub(/^ /, ""); print}' \
      "$headers_file" \
      | tr -d '\r' \
      | tail -1
  )"

  if [[ "$location" != http://localhost:3000* ]]; then
    echo "$label verification did not redirect to the local site URL." >&2
    exit 1
  fi

  printf '%s' "$location"
}

run_id="$(date +%s)-$$"
invite_email="ghos-invite-acceptance-$run_id@example.invalid"
recovery_email="ghos-recovery-acceptance-$run_id@example.invalid"
recovery_password="GHOS-recovery-old-$run_id"
replacement_password="GHOS-recovery-new-$run_id"

status="$(
  request_status \
    -X POST \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    -H "Content-Type: application/json" \
    -d "$(jq -nc --arg email "$invite_email" '{email:$email}')" \
    "$base_url/auth/v1/invite"
)"
assert_status "invitation request" "200" "$status"
invite_user_id="$(jq -r '.id // empty' "$response_file")"
[[ -n "$invite_user_id" ]] || {
  echo "Invitation response did not contain a user ID." >&2
  exit 1
}
fixture_user_ids+=("$invite_user_id")

invite_message_id="$(wait_for_message "$invite_email")"
fixture_message_ids+=("$invite_message_id")
invite_subject="$(
  curl -sS "$mailpit_url/api/v1/message/$invite_message_id" \
    | jq -r '.Subject'
)"
[[ "$invite_subject" == *"invited"* ]] || {
  echo "Captured invitation had an unexpected subject." >&2
  exit 1
}
echo "PASS: invitation email capture"

invite_location="$(verify_email_link "invitation email" "$invite_message_id")"
invite_access_token="$(
  printf '%s' "$invite_location" \
    | sed -nE 's/.*[#&]access_token=([^&]+).*/\1/p'
)"
[[ -n "$invite_access_token" ]] || {
  echo "Invitation verification redirect did not include a session." >&2
  exit 1
}
echo "PASS: invitation verification and session issuance"

status="$(
  request_status \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $invite_access_token" \
    "$base_url/auth/v1/user"
)"
assert_status "invited-user profile access" "200" "$status"

status="$(
  request_status \
    -X POST \
    -H "apikey: $service_role_key" \
    -H "Authorization: Bearer $service_role_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$recovery_email" \
        --arg password "$recovery_password" \
        '{email:$email,password:$password,email_confirm:true}'
    )" \
    "$base_url/auth/v1/admin/users"
)"
assert_status "recovery fixture creation" "200" "$status"
recovery_user_id="$(jq -r '.id // empty' "$response_file")"
[[ -n "$recovery_user_id" ]] || {
  echo "Recovery fixture response did not contain a user ID." >&2
  exit 1
}
fixture_user_ids+=("$recovery_user_id")

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(jq -nc --arg email "$recovery_email" '{email:$email}')" \
    "$base_url/auth/v1/recover"
)"
assert_status "password-recovery request" "200" "$status"

recovery_message_id="$(wait_for_message "$recovery_email")"
fixture_message_ids+=("$recovery_message_id")
recovery_subject="$(
  curl -sS "$mailpit_url/api/v1/message/$recovery_message_id" \
    | jq -r '.Subject'
)"
[[ "$recovery_subject" == *"Reset"* ]] || {
  echo "Captured recovery email had an unexpected subject." >&2
  exit 1
}
echo "PASS: password-recovery email capture"

recovery_location="$(
  verify_email_link "password-recovery email" "$recovery_message_id"
)"
recovery_access_token="$(
  printf '%s' "$recovery_location" \
    | sed -nE 's/.*[#&]access_token=([^&]+).*/\1/p'
)"
[[ -n "$recovery_access_token" ]] || {
  echo "Recovery verification redirect did not include a session." >&2
  exit 1
}
echo "PASS: password-recovery verification and session issuance"

status="$(
  request_status \
    -X PUT \
    -H "apikey: $anon_key" \
    -H "Authorization: Bearer $recovery_access_token" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc --arg password "$replacement_password" '{password:$password}'
    )" \
    "$base_url/auth/v1/user"
)"
assert_status "recovered password replacement" "200" "$status"

status="$(
  request_status \
    -X POST \
    -H "apikey: $anon_key" \
    -H "Content-Type: application/json" \
    -d "$(
      jq -nc \
        --arg email "$recovery_email" \
        --arg password "$replacement_password" \
        '{email:$email,password:$password}'
    )" \
    "$base_url/auth/v1/token?grant_type=password"
)"
assert_status "sign-in with recovered password" "200" "$status"

cleanup
trap - EXIT

remaining_fixture_count="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres -Atc \
    "
      select count(*)
      from auth.users
      where email in ('$invite_email', '$recovery_email');
    "
)"

remaining_message_count="$(
  curl -sS "$mailpit_url/api/v1/messages" \
    | jq --arg invite "$invite_email" --arg recovery "$recovery_email" \
      '[
         .messages[]
         | select(
             any(.To[]?;
               .Address == $invite or .Address == $recovery
             )
           )
       ]
       | length'
)"

if [[ "$remaining_fixture_count" != "0" \
      || "$remaining_message_count" != "0" ]]; then
  echo "Disposable Auth email fixtures remained after cleanup." >&2
  exit 1
fi

echo "Local-Delivery Auth invitation and recovery acceptance passed."
