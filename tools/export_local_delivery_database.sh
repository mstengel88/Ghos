#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"

export SUPABASE_EXPORT_PROJECT_REF="mtntrlbuhcbdrngiubdu"
export SUPABASE_EXPORT_POOLER_HOST="aws-0-us-west-2.pooler.supabase.com"
export SUPABASE_EXPORT_LABEL="Local Delivery"
export SUPABASE_EXPORT_ARCHIVE_SLUG="local-delivery"
export SUPABASE_EXPORT_KEYCHAIN_ACCOUNT="local-delivery"
export SUPABASE_EXPORT_EXPECTED_COUNTS_SQL_FILE="$repo_root/migration/supabase/candidates/local-delivery/expected-counts.sql"

exec "$repo_root/tools/export_winterwatch_database.sh"
