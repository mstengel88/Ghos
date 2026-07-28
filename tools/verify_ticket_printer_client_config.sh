#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${TICKET_PRINTER_SOURCE_ROOT:-/Users/mattstengel/edit-my-ticket}"
client_file="$source_root/src/integrations/supabase/client.ts"
function_file="$source_root/supabase/functions/loadrite-sync/index.ts"
example_file="$source_root/.env.example"

for source_file in \
  "$client_file" \
  "$function_file" \
  "$example_file" \
  "$source_root/.gitignore"; do
  if [[ ! -f "$source_file" ]]; then
    echo "Required Ticket Printer configuration is missing: $source_file" >&2
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

assert_contains "$client_file" 'import.meta.env.VITE_SUPABASE_URL'
assert_contains "$client_file" 'import.meta.env.VITE_SUPABASE_PUBLISHABLE_KEY'
assert_contains "$function_file" 'Deno.env.get("SUPABASE_URL")'
assert_contains "$example_file" \
  'VITE_SUPABASE_URL=https://your-self-hosted-supabase.example'
assert_contains "$example_file" \
  'VITE_SUPABASE_PUBLISHABLE_KEY=replace-with-the-runtime-publishable-key'

if ! git -C "$source_root" check-ignore -q .env; then
  echo "The private Ticket Printer runtime environment file is not ignored." >&2
  exit 1
fi
if git -C "$source_root" ls-files --error-unmatch .env >/dev/null 2>&1; then
  echo "The private Ticket Printer runtime environment file is tracked." >&2
  exit 1
fi
if ! git -C "$source_root" ls-files --error-unmatch .env.example \
    >/dev/null 2>&1; then
  echo "The safe Ticket Printer environment example is not tracked." >&2
  exit 1
fi

if git -C "$source_root" grep -n \
    -E '[a-z]{20}\.supabase\.co|dlayrpnmfnbjlxgnkczv' \
    -- src supabase/functions \
    >/dev/null 2>&1; then
  echo "A managed Supabase project is embedded in Ticket Printer runtime source." >&2
  git -C "$source_root" grep -n \
    -E '[a-z]{20}\.supabase\.co|dlayrpnmfnbjlxgnkczv' \
    -- src supabase/functions >&2 || true
  exit 1
fi

echo "Ticket Printer Supabase client configuration acceptance passed."
echo "Browser and Loadrite runtime endpoints are environment-based."
echo "Private environment files are ignored; the tracked example is secret-free."
