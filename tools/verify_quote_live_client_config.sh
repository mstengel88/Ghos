#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${QUOTE_LIVE_SOURCE_ROOT:-/Users/mattstengel/local-contractor}"

required_files=(
  "$source_root/app/lib/supabase.server.ts"
  "$source_root/app/lib/user-auth.server.ts"
  "$source_root/.env.contractor.example"
  "$source_root/docker-compose.contractor.yml"
  "$source_root/.gitignore"
)
for source_file in "${required_files[@]}"; do
  if [[ ! -f "$source_file" ]]; then
    echo "Required Quote Live configuration is missing: $source_file" >&2
    exit 1
  fi
done

assert_contains() {
  local file="$1"
  local fragment="$2"
  if ! grep -Fq "$fragment" "$file"; then
    echo "$(basename "$file") is missing required configuration: $fragment" >&2
    exit 1
  fi
}

assert_contains "$source_root/app/lib/supabase.server.ts" \
  'process.env.SUPABASE_URL'
assert_contains "$source_root/app/lib/supabase.server.ts" \
  'process.env.SUPABASE_SERVICE_ROLE_KEY'
assert_contains "$source_root/app/lib/user-auth.server.ts" \
  'process.env.SUPABASE_ANON_KEY'
assert_contains "$source_root/docker-compose.contractor.yml" \
  '.env.contractor'
assert_contains "$source_root/.env.contractor.example" \
  'SUPABASE_URL=https://your-project.supabase.co'
assert_contains "$source_root/.env.contractor.example" \
  'SUPABASE_ANON_KEY=replace-with-supabase-anon-key'
assert_contains "$source_root/.env.contractor.example" \
  'SUPABASE_SERVICE_ROLE_KEY=replace-with-supabase-service-role-key'

if ! git -C "$source_root" check-ignore -q .env.contractor; then
  echo "The private Quote Live runtime environment file is not ignored." >&2
  exit 1
fi
if git -C "$source_root" ls-files --error-unmatch .env.contractor \
    >/dev/null 2>&1; then
  echo "The private Quote Live runtime environment file is tracked." >&2
  exit 1
fi
if git -C "$source_root" grep -n \
    -E '[a-z]{20}\\.supabase\\.co|dbyxbgbkokcddgeybjmf' \
    -- app Dockerfile 'docker-compose*.yml' shopify.web.toml \
    >/dev/null 2>&1; then
  echo "A managed Supabase project URL is embedded in tracked runtime source." >&2
  git -C "$source_root" grep -n \
    -E '[a-z]{20}\\.supabase\\.co|dbyxbgbkokcddgeybjmf' \
    -- app Dockerfile 'docker-compose*.yml' shopify.web.toml >&2 || true
  exit 1
fi

echo "Quote Live Supabase client configuration acceptance passed."
echo "Runtime URL, anonymous key, and service credential are environment-based."
echo "The private environment file is ignored and is not tracked."
