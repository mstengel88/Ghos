#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
target="$script_dir/functions.env"

if [[ ! -f "$target" ]]; then
  printf 'Run ./generate-env.sh before generating Dump Site secrets.\n' >&2
  exit 1
fi

set_if_empty() {
  local name="$1" value="$2"
  python3 - "$target" "$name" "$value" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
name = sys.argv[2]
value = sys.argv[3]
lines = path.read_text().splitlines()
for index, line in enumerate(lines):
    if line.startswith(f"{name}="):
        if line != f"{name}=":
            raise SystemExit(0)
        lines[index] = f"{name}={value}"
        break
else:
    lines.append(f"{name}={value}")
path.write_text("\n".join(lines) + "\n")
PY
}

set_if_empty DUMP_SITE_QR_TOKEN "$(openssl rand -hex 32)"
set_if_empty DUMP_SITE_BRIDGE_SECRET "$(openssl rand -hex 48)"
chmod 600 "$target"

printf 'Dump Site QR and bridge secrets are present in the private integration file.\n'
printf 'Their values were not displayed. Client and bridge cutover must use the same values.\n'
