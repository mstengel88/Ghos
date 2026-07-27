#!/usr/bin/env bash
set -euo pipefail
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
secret_file="${repo_root}/migration/supabase/secrets/managed-db.env"
export_root="${repo_root}/migration/supabase/exports/database"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"

if [[ ! -f "${secret_file}" ]]; then
  echo "Missing ${secret_file}"
  echo "Copy migration/supabase/managed-db.env.example there and add the connection URLs."
  exit 1
fi

# shellcheck disable=SC1090
source "${secret_file}"

projects=(
  "ticket-printer:TICKET_PRINTER_DB_URL"
  "winterwatch:WINTERWATCH_DB_URL"
  "help-desk:HELP_DESK_DB_URL"
  "greenhills-quote-live:GREENHILLS_QUOTE_LIVE_DB_URL"
  "local-delivery:LOCAL_DELIVERY_DB_URL"
  "dump-site:DUMP_SITE_DB_URL"
)

mkdir -p "${export_root}/${timestamp}"

for item in "${projects[@]}"; do
  project_name="${item%%:*}"
  variable_name="${item##*:}"
  connection_url="${!variable_name:-}"

  if [[ -z "${connection_url}" ]]; then
    echo "${project_name}: skipped (connection URL not configured)"
    continue
  fi

  project_root="${export_root}/${timestamp}/${project_name}"
  mkdir -p "${project_root}"

  echo "${project_name}: exporting roles"
  supabase db dump \
    --db-url "${connection_url}" \
    --file "${project_root}/roles.sql" \
    --role-only

  echo "${project_name}: exporting schema"
  supabase db dump \
    --db-url "${connection_url}" \
    --file "${project_root}/schema.sql"

  echo "${project_name}: exporting data"
  supabase db dump \
    --db-url "${connection_url}" \
    --file "${project_root}/data.sql" \
    --data-only \
    --use-copy

  (
    cd "${project_root}"
    shasum -a 256 roles.sql schema.sql data.sql > SHA256SUMS
  )

  echo "${project_name}: export complete"
done

echo "Encrypted storage is still required for ${export_root}/${timestamp}."
