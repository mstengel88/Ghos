#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
baseline_root="$repo_root/migration/supabase/baselines/local-delivery"
quote_root="${QUOTE_SOURCE_ROOT:-/Users/mattstengel/local-contractor}"
dispatch_root="${DISPATCH_V2_SOURCE_ROOT:-/Users/mattstengel/local-delivery/dispatch-v2-sandbox}"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="${SUPABASE_DATABASE_NAME:-postgres}"

if [[ ! "$database_name" =~ ^[a-zA-Z0-9_]+$ ]]; then
  echo "Refusing invalid rehearsal database name: $database_name" >&2
  exit 1
fi

sources=(
  "$baseline_root/000_foundation.sql"
  "$quote_root/dispatch_schema.sql"
  "$quote_root/supabase_auth_schema.sql"
  "$dispatch_root/sql/phase3_reliability.sql"
  "$dispatch_root/sql/driver_user_links.sql"
  "$quote_root/supabase_security_hardening.sql"
  "$dispatch_root/sql/dispatch_b2b_companies_rls.sql"
  "$baseline_root/850_live_indexes.sql"
  "$baseline_root/900_live_contract.sql"
  "$repo_root/migration/supabase/candidates/local-delivery/001_api_grants.sql"
)

for source_file in "${sources[@]}"; do
  if [[ ! -f "$source_file" ]]; then
    echo "Missing required source: $source_file" >&2
    exit 1
  fi
done

for source_file in "${sources[@]}"; do
  echo "Applying $(basename "$source_file")"
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
    < "$source_file"
done

echo "Local-Delivery schema rehearsal applied."
