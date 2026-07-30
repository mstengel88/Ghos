#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"

export SUPABASE_EXPORT_PROJECT_REF="dbyxbgbkokcddgeybjmf"
export SUPABASE_EXPORT_POOLER_HOST="aws-0-us-west-2.pooler.supabase.com"
export SUPABASE_EXPORT_LABEL="GreenHills Quote Live"
export SUPABASE_EXPORT_ARCHIVE_SLUG="greenhills-quote-live"
export SUPABASE_EXPORT_KEYCHAIN_ACCOUNT="greenhills-quote-live"
export SUPABASE_EXPORT_EXPECTED_COUNTS_SQL_FILE="$repo_root/migration/supabase/candidates/greenhills-quote-live/expected-counts.sql"

exec "$repo_root/tools/export_winterwatch_database.sh"
