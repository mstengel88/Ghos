#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$script_dir/functions.env"

if [[ ! -f "$target" ]]; then
  cp "$script_dir/functions.env.example" "$target"
fi
chmod 600 "$target"

read_env() {
  local file="$1" name="$2"
  [[ -r "$file" ]] || return 0
  sed -n "s/^${name}=//p" "$file" | tail -n 1
}

set_env() {
  local name="$1" value="$2"
  [[ -n "$value" ]] || return 0
  python3 - "$target" "$name" "$value" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
name = sys.argv[2]
value = sys.argv[3]
lines = path.read_text().splitlines()
replacement = f"{name}={value}"
for index, line in enumerate(lines):
    if line.startswith(f"{name}="):
        if line != f"{name}=":
            raise SystemExit(0)
        lines[index] = replacement
        break
else:
    lines.append(replacement)
path.write_text("\n".join(lines) + "\n")
PY
}

main_env="${GHOS_MAIN_ENV:-/opt/ghos/.env}"
shipcalc_env="${SHIPCALC_ENV:-/opt/ghos/apps/shipcalc2/.env}"
ticket_env="${TICKET_CREATOR_ENV:-/opt/ghos/apps/ticket-creator/.env}"

# Copy only already-approved server-side values. Nothing is printed.
google_key="$(read_env "$main_env" GOOGLE_MAPS_API_KEY)"
[[ -n "$google_key" ]] || google_key="$(read_env "$shipcalc_env" GOOGLE_MAPS_API_KEY)"
set_env GOOGLE_MAPS_API_KEY "$google_key"
set_env SHOPIFY_API_KEY "$(read_env "$shipcalc_env" SHOPIFY_CLIENT_ID)"
set_env SHOPIFY_API_SECRET "$(read_env "$shipcalc_env" SHOPIFY_CLIENT_SECRET)"
set_env LOADRITE_SYNC_USER_ID "$(read_env "$ticket_env" LOADRITE_SYNC_USER_ID)"

printf 'Imported available server-side integration values without displaying them.\n'
printf 'Run ./validate-integrations.sh to see which approved secrets still need to be entered.\n'
