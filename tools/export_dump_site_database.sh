#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"

export SUPABASE_EXPORT_PROJECT_REF="bnethnlrhwcjgjgjvoxz"
export SUPABASE_EXPORT_POOLER_HOST="${SUPABASE_EXPORT_POOLER_HOST:-aws-1-us-west-2.pooler.supabase.com}"
export SUPABASE_EXPORT_LABEL="Dump Site"
export SUPABASE_EXPORT_ARCHIVE_SLUG="dump-site"
export SUPABASE_EXPORT_KEYCHAIN_ACCOUNT="dump-site"
export SUPABASE_EXPORT_EXPECTED_COUNTS_SQL_FILE="$repo_root/migration/supabase/candidates/dump-site/expected-counts.sql"

exec "$repo_root/tools/export_winterwatch_database.sh"
