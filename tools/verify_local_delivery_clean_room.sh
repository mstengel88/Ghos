#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"

if [[ "$db_container" != "supabase-db" ]]; then
  echo "Refusing verification for unexpected container: $db_container" >&2
  exit 1
fi

container_status="$(
  docker inspect \
    --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' \
    "$db_container"
)"

if [[ "$container_status" != "running|healthy" ]]; then
  echo "Local Supabase database is not healthy: $container_status" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
  < "$repo_root/migration/supabase/sql/verify_local_delivery_contract.sql"

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
  < "$repo_root/migration/supabase/sql/prepare_reconciliation_staging.sql"

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
  < "$repo_root/migration/supabase/sql/verify_local_delivery_rls.sql"

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
  < "$repo_root/migration/supabase/sql/verify_reconciliation_classification.sql"

staging_row_count="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres -Atc \
    "
      select
        (select count(*) from migration_reconcile.import_batches)
        + (select count(*) from migration_reconcile.source_rows)
        + (select count(*) from migration_reconcile.merge_decisions);
    "
)"

if [[ "$staging_row_count" != "0" ]]; then
  echo "Reconciliation staging is not empty: $staging_row_count total row(s)" >&2
  exit 1
fi

echo "Local-Delivery clean-room contract, RLS, and reconciliation verification passed."
