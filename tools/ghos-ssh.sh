#!/usr/bin/env bash
set -Eeuo pipefail

ghos_host="${GHOS_SSH_HOST:-100.75.152.30}"
ghos_user="${GHOS_SSH_USER:-ghosadmin}"
ghos_key="${GHOS_SSH_KEY:-$HOME/.ssh/ghos_codex_ed25519}"

if [[ ! -r "$ghos_key" ]]; then
  printf 'GHOS SSH key is missing or unreadable: %s\n' "$ghos_key" >&2
  exit 1
fi

exec ssh \
  -o BatchMode=yes \
  -o IdentitiesOnly=yes \
  -o ConnectTimeout=10 \
  -i "$ghos_key" \
  "$ghos_user@$ghos_host" \
  "$@"
