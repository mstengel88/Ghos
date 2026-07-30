#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
run_id="$(date -u +%Y%m%d_%H%M%S)"
canonical_database="ghos_merge_rehearsal_local_$run_id"
legacy_database="ghos_merge_rehearsal_quote_$run_id"
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-merge-rehearsal.XXXXXX")"
reconciliation_log="$work_root/reconciliation.log"

database_exists() {
  docker exec -i "$db_container" \
    psql -X -v ON_ERROR_STOP=1 -U "$database_admin" -d template1 -Atqc \
      "select 1 from pg_database where datname = '$1'" |
    grep -qx 1
}

drop_database() {
  local database_name="$1"
  if database_exists "$database_name"; then
    docker exec "$db_container" \
      dropdb -U "$database_admin" --if-exists --force "$database_name" \
      >/dev/null
  fi
}

cleanup() {
  set +e
  drop_database "$legacy_database"
  drop_database "$canonical_database"
  rm -rf "$work_root"
}
trap cleanup EXIT

if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi

if ! GHOS_RECONCILE_CANONICAL_DATABASE="$canonical_database" \
  GHOS_RECONCILE_LEGACY_DATABASE="$legacy_database" \
  GHOS_RECONCILE_RETAIN_DATABASES=1 \
  GHOS_RECONCILE_APPLY_OWNER_QUOTE_DISPOSITION=1 \
    "$repo_root/tools/reconcile_local_delivery_quote_live_snapshots.sh" \
    > "$reconciliation_log" 2>&1; then
  printf 'Exact reconciliation preparation failed:\n' >&2
  tail -n 160 "$reconciliation_log" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" \
  < "$repo_root/migration/supabase/sql/apply_reconciliation_to_clone.sql"

docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      select 'dispatch_notifications' as table_name, count(*) as rows
      from public.dispatch_notifications
      union all
      select 'custom_delivery_quotes', count(*)
      from public.custom_delivery_quotes
      union all
      select 'dispatch_orders', count(*)
      from public.dispatch_orders;
    "

printf 'Local-Delivery notification-only merge rehearsal passed.\n'
printf 'No Quote Live quote was imported and no production database was written.\n'
