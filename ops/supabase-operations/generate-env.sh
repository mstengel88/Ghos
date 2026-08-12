#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
env_file="$script_dir/.env"
functions_env="$script_dir/functions.env"

if [[ -e "$env_file" || -e "$functions_env" ]]; then
  printf 'Refusing to overwrite an existing .env or functions.env.\n' >&2
  exit 1
fi

# This password is interpolated into PostgreSQL connection URIs by Compose.
# Hex keeps it high-entropy while avoiding URI delimiters such as /, +, @,
# and : that would otherwise require a separately percent-encoded value.
postgres_password="$(openssl rand -hex 48)"
jwt_secret="$(openssl rand -base64 48 | tr -d '\n')"

sign_jwt() {
  local role="$1"
  JWT_ROLE="$role" JWT_SECRET_VALUE="$jwt_secret" python3 <<'PY'
import base64, hashlib, hmac, json, os, time
def enc(value):
    return base64.urlsafe_b64encode(value).rstrip(b'=')
header = enc(json.dumps({'alg': 'HS256', 'typ': 'JWT'}, separators=(',', ':')).encode())
payload = enc(json.dumps({
    'role': os.environ['JWT_ROLE'],
    'iss': 'supabase',
    'iat': int(time.time()),
    'exp': int(time.time()) + (10 * 365 * 24 * 60 * 60),
}, separators=(',', ':')).encode())
message = header + b'.' + payload
signature = enc(hmac.new(os.environ['JWT_SECRET_VALUE'].encode(), message, hashlib.sha256).digest())
print((message + b'.' + signature).decode())
PY
}

anon_key="$(sign_jwt anon)"
service_key="$(sign_jwt service_role)"

sed \
  -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$postgres_password|" \
  -e "s|^JWT_SECRET=.*|JWT_SECRET=$jwt_secret|" \
  -e "s|^ANON_KEY=.*|ANON_KEY=$anon_key|" \
  -e "s|^SERVICE_ROLE_KEY=.*|SERVICE_ROLE_KEY=$service_key|" \
  "$script_dir/operations.env.example" > "$env_file"
cp "$script_dir/functions.env.example" "$functions_env"
chmod 600 "$env_file" "$functions_env"
printf '%s\n' \
  'Created root-private runtime configuration.' \
  'Complete functions.env and replace the URL/SMTP placeholders in .env before cutover.'
