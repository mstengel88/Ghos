#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_admin="${SUPABASE_DATABASE_ADMIN:-supabase_admin}"
use_existing="${GHOS_RECONCILE_USE_EXISTING_DATABASES:-0}"
run_id="$(date -u +%Y%m%d_%H%M%S)"
canonical_database="${GHOS_RECONCILE_CANONICAL_DATABASE:-ghos_reconcile_local_$run_id}"
legacy_database="${GHOS_RECONCILE_LEGACY_DATABASE:-ghos_reconcile_quote_$run_id}"
canonical_archive="$(
  find "$repo_root/migration/supabase/exports/local-delivery" \
    -type f -name 'local-delivery-database.sql.tar.gz.enc' \
    -print 2>/dev/null |
    sort |
    tail -n 1
)"
legacy_archive="$(
  find "$repo_root/migration/supabase/exports/greenhills-quote-live" \
    -type f -name 'greenhills-quote-live-database.sql.tar.gz.enc' \
    -print 2>/dev/null |
    sort |
    tail -n 1
)"
created_canonical=0
created_legacy=0
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-reconcile.XXXXXX")"
canonical_inventory="$work_root/canonical-inventory.tsv"
legacy_inventory="$work_root/legacy-inventory.tsv"
all_tables_inventory="$work_root/all-tables.txt"

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
  if [[ "$created_legacy" == 1 ]]; then
    drop_database "$legacy_database"
  fi
  if [[ "$created_canonical" == 1 ]]; then
    drop_database "$canonical_database"
  fi
  rm -rf "$work_root"
}
trap cleanup EXIT

for command_name in docker shasum; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  fi
done
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
  exit 1
fi
for database_name in "$canonical_database" "$legacy_database"; do
  if [[ ! "$database_name" =~ ^[a-z_][a-z0-9_]*$ ]]; then
    printf 'Unsafe reconciliation database name: %s\n' "$database_name" >&2
    exit 1
  fi
done
if [[ "$canonical_database" == "$legacy_database" ]]; then
  printf 'Canonical and legacy database names must differ.\n' >&2
  exit 1
fi
if [[ "$use_existing" != "0" && "$use_existing" != "1" ]]; then
  printf 'GHOS_RECONCILE_USE_EXISTING_DATABASES must be 0 or 1.\n' >&2
  exit 1
fi
if [[ ! -s "$canonical_archive" || ! -s "$legacy_archive" ]]; then
  printf 'Both encrypted Local-Delivery and Quote Live exports are required.\n' >&2
  exit 1
fi

if [[ "$use_existing" == "1" ]]; then
  if ! database_exists "$canonical_database" ||
    ! database_exists "$legacy_database"; then
    printf 'The requested existing reconciliation databases were not found.\n' >&2
    exit 1
  fi
else
  if database_exists "$canonical_database" ||
    database_exists "$legacy_database"; then
    printf 'A reconciliation database already exists; refusing to overwrite it.\n' >&2
    exit 1
  fi

  created_canonical=1
  LOCAL_DELIVERY_DATABASE_ARCHIVE="$canonical_archive" \
    LOCAL_DELIVERY_REHEARSAL_DATABASE="$canonical_database" \
    LOCAL_DELIVERY_RETAIN_REHEARSAL_DATABASE=1 \
    "$repo_root/tools/rehearse_local_delivery_production_restore.sh"

  created_legacy=1
  GREENHILLS_QUOTE_LIVE_DATABASE_ARCHIVE="$legacy_archive" \
    GREENHILLS_QUOTE_LIVE_REHEARSAL_DATABASE="$legacy_database" \
    GREENHILLS_QUOTE_LIVE_RETAIN_REHEARSAL_DATABASE=1 \
    "$repo_root/tools/rehearse_greenhills_quote_live_restore.sh"
fi

docker exec -i "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" -d "$canonical_database" \
  < "$repo_root/migration/supabase/sql/prepare_reconciliation_staging.sql" \
  >/dev/null
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -c "
      truncate table
        migration_reconcile.merge_decisions,
        migration_reconcile.identity_map,
        migration_reconcile.source_rows,
        migration_reconcile.source_tables,
        migration_reconcile.import_batches;
    " >/dev/null

natural_key_expression() {
  case "$1" in
    app_settings | dispatch_settings)
      printf '%s\n' 't.key'
      ;;
    app_user_profiles | dispatch_user_roles)
      printf '%s\n' 'lower(btrim(t.email))'
      ;;
    product_source_map)
      printf '%s\n' 'lower(btrim(t.sku))'
      ;;
    shipping_material_rules)
      printf '%s\n' 't.prefix'
      ;;
    shopify_app_settings)
      printf '%s\n' 'lower(btrim(t.shop))'
      ;;
    *)
      printf '\n'
      ;;
  esac
}

table_inventory() {
  local database_name="$1"
  docker exec "$db_container" \
    psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
      -d "$database_name" -AtF $'\t' -c "
        select
          table_name,
          coalesce(
            (
              select string_agg(key_column.column_name, ',' order by key_column.ordinal_position)
              from information_schema.table_constraints constraint_row
              join information_schema.key_column_usage key_column
                on key_column.constraint_schema = constraint_row.constraint_schema
                and key_column.constraint_name = constraint_row.constraint_name
                and key_column.table_schema = constraint_row.table_schema
                and key_column.table_name = constraint_row.table_name
              where constraint_row.table_schema = table_row.table_schema
                and constraint_row.table_name = table_row.table_name
                and constraint_row.constraint_type = 'PRIMARY KEY'
            ),
            ''
          )
        from information_schema.tables table_row
        where table_schema = 'public'
          and table_type = 'BASE TABLE'
        order by table_name;
      "
}

table_inventory "$canonical_database" > "$canonical_inventory"
table_inventory "$legacy_database" > "$legacy_inventory"
cut -f 1 "$canonical_inventory" "$legacy_inventory" |
  sort -u > "$all_tables_inventory"

canonical_table_count="$(wc -l < "$canonical_inventory" | tr -d ' ')"
legacy_table_count="$(wc -l < "$legacy_inventory" | tr -d ' ')"
if [[ "$legacy_table_count" != "22" ]]; then
  printf 'Expected 22 Quote Live public tables, found %s.\n' \
    "$legacy_table_count" >&2
  exit 1
fi
if [[ "$canonical_table_count" != "23" ]]; then
  printf 'Expected 23 Local-Delivery public tables, found %s.\n' \
    "$canonical_table_count" >&2
  exit 1
fi

load_source_table() {
  local source_project="$1"
  local database_name="$2"
  local table_name="$3"
  local primary_key="$4"
  local key_expression
  local duplicate_count
  local row_count

  key_expression="$(natural_key_expression "$table_name")"

  if [[ -z "$key_expression" ]]; then
    if [[ -z "$primary_key" || "$primary_key" == *","* ]]; then
      printf 'No safe record key is configured for %s.%s.\n' \
        "$source_project" "$table_name" >&2
      exit 1
    fi
    key_expression="t.\"$primary_key\""
  fi

  duplicate_count="$(
    docker exec "$db_container" \
      psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
        -d "$database_name" -Atqc "
          select count(*)
          from (
            select ($key_expression)::text
            from public.\"$table_name\" t
            group by ($key_expression)::text
            having count(*) > 1
          ) duplicate_keys;
        "
  )"
  if [[ "$duplicate_count" != "0" ]]; then
    printf '%s.%s has %s duplicate reconciliation key(s).\n' \
      "$source_project" "$table_name" "$duplicate_count" >&2
    exit 1
  fi
  row_count="$(
    docker exec "$db_container" \
      psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
        -d "$database_name" -Atqc "
          select count(*) from public.\"$table_name\";
        "
  )"

  docker exec -i "$db_container" \
    psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
      -d "$canonical_database" \
      -v source_project="$source_project" \
      -v table_name="$table_name" \
      -v row_count="$row_count" \
      -v key_strategy="$key_expression" <<'SQL' >/dev/null
insert into migration_reconcile.source_tables (
  source_project,
  table_name,
  source_row_count,
  record_key_strategy
)
values (
  :'source_project',
  :'table_name',
  :'row_count'::bigint,
  :'key_strategy'
);
SQL

  docker exec "$db_container" \
    psql -X -q -v ON_ERROR_STOP=1 -U "$database_admin" \
      -d "$database_name" -c "
        copy (
          select
            '$source_project'::text,
            '$table_name'::text,
            ($key_expression)::text,
            to_jsonb(t)::text
          from public.\"$table_name\" t
          order by ($key_expression)::text
        ) to stdout with (format csv)
      " |
    docker exec -i "$db_container" \
        psql -X -q -v ON_ERROR_STOP=1 -U "$database_admin" \
        -d "$canonical_database" -c "
          copy migration_reconcile.source_rows (
            source_project,
            table_name,
            record_key,
            payload
          ) from stdin with (format csv)
        " >/dev/null
}

while IFS= read -r table_name; do
  canonical_primary_key="$(
    awk -F $'\t' -v table="$table_name" \
      '$1 == table { print $2; exit }' "$canonical_inventory"
  )"
  legacy_primary_key="$(
    awk -F $'\t' -v table="$table_name" \
      '$1 == table { print $2; exit }' "$legacy_inventory"
  )"
  if grep -q "^${table_name}"$'\t' "$canonical_inventory"; then
    load_source_table \
      local_delivery \
      "$canonical_database" \
      "$table_name" \
      "$canonical_primary_key"
  fi
  if grep -q "^${table_name}"$'\t' "$legacy_inventory"; then
    load_source_table \
      quote_live \
      "$legacy_database" \
      "$table_name" \
      "$legacy_primary_key"
  fi
done < "$all_tables_inventory"

canonical_sha="$(
  shasum -a 256 "$canonical_archive" |
    awk '{print $1}'
)"
legacy_sha="$(
  shasum -a 256 "$legacy_archive" |
    awk '{print $1}'
)"

docker exec -i "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" \
    -v canonical_sha="$canonical_sha" \
    -v legacy_sha="$legacy_sha" \
    -v canonical_tables="$canonical_table_count" \
    -v legacy_tables="$legacy_table_count" <<'SQL' >/dev/null
insert into migration_reconcile.import_batches (
  source_project,
  extracted_at,
  source_table_count,
  source_row_count,
  manifest_sha256
)
select
  'local_delivery',
  now(),
  :'canonical_tables'::integer,
  coalesce(sum(source_row_count), 0),
  :'canonical_sha'
from migration_reconcile.source_tables
where source_project = 'local_delivery'
union all
select
  'quote_live',
  now(),
  :'legacy_tables'::integer,
  coalesce(sum(source_row_count), 0),
  :'legacy_sha'
from migration_reconcile.source_tables
where source_project = 'quote_live';
SQL

printf '\nExact encrypted-snapshot reconciliation summary\n'
printf '%s\n' '==============================================='
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      select *
      from migration_reconcile.reconciliation_summary;
    "

printf '\nConflicts after excluding volatile timestamps\n'
printf '%s\n' '============================================'
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      with normalized as (
        select
          table_name,
          record_key,
          case
            when table_name = 'Session' then '{}'::jsonb
            else canonical_payload - (
              array['created_at', 'updated_at', 'last_seen_at']
              || case
                when table_name in ('app_audit_log', 'custom_delivery_quotes')
                  then array['actor_user_id', 'created_by_user_id']
                when table_name in ('app_user_profiles', 'product_source_map')
                  then array['id']
                else array[]::text[]
              end
            )
          end as canonical_payload,
          case
            when table_name = 'Session' then '{}'::jsonb
            else legacy_payload - (
              array['created_at', 'updated_at', 'last_seen_at']
              || case
                when table_name in ('app_audit_log', 'custom_delivery_quotes')
                  then array['actor_user_id', 'created_by_user_id']
                when table_name in ('app_user_profiles', 'product_source_map')
                  then array['id']
                else array[]::text[]
              end
            )
          end as legacy_payload
        from migration_reconcile.record_comparison
        where classification = 'conflict'
      ),
      compared as (
        select
          table_name,
          migration_reconcile.shared_jsonb_projection(
            canonical_payload,
            legacy_payload
          ) is distinct from
          migration_reconcile.shared_jsonb_projection(
            legacy_payload,
            canonical_payload
          ) as substantive
        from normalized
      )
      select
        table_name,
        count(*) filter (where substantive) as substantive_conflicts,
        count(*) filter (where not substantive) as timestamp_only_conflicts
      from compared
      group by table_name
      order by table_name;
    "

printf '\nDiffering shared fields (counts only)\n'
printf '%s\n' '===================================='
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      with conflict_fields as (
        select
          comparison.table_name,
          field.key as field_name
        from migration_reconcile.record_comparison comparison
        cross join lateral jsonb_each(comparison.canonical_payload) field
        where comparison.classification = 'conflict'
          and comparison.legacy_payload ? field.key
          and field.value is distinct from comparison.legacy_payload -> field.key
      )
      select table_name, field_name, count(*) as differing_records
      from conflict_fields
      group by table_name, field_name
      order by table_name, differing_records desc, field_name;
    "

printf '\nIdentity and quote-owner reconciliation gates\n'
printf '%s\n' '============================================='
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      with canonical_people as (
        select
          lower(btrim(payload ->> 'email')) as email,
          payload ->> 'id' as user_id
        from migration_reconcile.source_rows
        where source_project = 'local_delivery'
          and table_name = 'app_user_profiles'
      ),
      legacy_people as (
        select
          lower(btrim(payload ->> 'email')) as email,
          payload ->> 'id' as user_id
        from migration_reconcile.source_rows
        where source_project = 'quote_live'
          and table_name = 'app_user_profiles'
      ),
      creator_resolution as (
        select
          quote.record_key,
          case
            when nullif(quote.payload ->> 'created_by_user_id', '') is null
              then 'not_required'
            when canonical.user_id is not null then 'mapped_by_email'
            else 'unmapped'
          end as resolution
        from migration_reconcile.source_rows quote
        left join legacy_people legacy
          on legacy.user_id = quote.payload ->> 'created_by_user_id'
        left join canonical_people canonical
          on canonical.email = legacy.email
        where quote.source_project = 'quote_live'
          and quote.table_name = 'custom_delivery_quotes'
      )
      select 'profile_email_overlap' as gate, count(*)::bigint as records
      from canonical_people canonical
      join legacy_people legacy using (email)
      union all
      select 'profile_uuid_rewrites', count(*)
      from canonical_people canonical
      join legacy_people legacy using (email)
      where canonical.user_id is distinct from legacy.user_id
      union all
      select 'quote_creators_mapped_by_email', count(*)
      from creator_resolution
      where resolution = 'mapped_by_email'
      union all
      select 'quote_creators_unmapped', count(*)
      from creator_resolution
      where resolution = 'unmapped'
      union all
      select 'quote_creator_not_required', count(*)
      from creator_resolution
      where resolution = 'not_required';
    "

docker exec -i "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" \
  < "$repo_root/migration/supabase/sql/seed_reconciliation_policy_decisions.sql" \
  >/dev/null

printf '\nDocumented policy decisions\n'
printf '%s\n' '==========================='
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      select decision, count(*) as records
      from migration_reconcile.merge_decisions
      where decided_by = 'documented-owner-policy-v1'
      group by decision
      order by decision;
    "

printf '\nRemaining human review queue\n'
printf '%s\n' '============================'
docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" -P pager=off -c "
      select table_name, classification, count(*) as records
      from migration_reconcile.unresolved_records
      group by table_name, classification
      order by table_name, classification;
    "

docker exec "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" \
    -d "$canonical_database" <<'SQL' >/dev/null
do $$
declare
  policy_decision_count bigint;
  unresolved_quote_count bigint;
  unresolved_nonquote_count bigint;
begin
  select count(*)
  into policy_decision_count
  from migration_reconcile.merge_decisions
  where decided_by = 'documented-owner-policy-v1';

  select
    count(*) filter (where table_name = 'custom_delivery_quotes'),
    count(*) filter (where table_name <> 'custom_delivery_quotes')
  into unresolved_quote_count, unresolved_nonquote_count
  from migration_reconcile.unresolved_records;

  if policy_decision_count <> 412 then
    raise exception
      'Exact policy baseline changed: expected 412 decisions, found %',
      policy_decision_count;
  end if;

  if unresolved_nonquote_count <> 0 then
    raise exception
      'Policy scaffold left % non-quote record(s) unresolved',
      unresolved_nonquote_count;
  end if;

  if unresolved_quote_count <> 4 then
    raise exception
      'Exact quote review baseline changed: expected 4 records, found %',
      unresolved_quote_count;
  end if;

  raise notice
    'Exact policy baseline verified: 412 decisions and four quotes for human review.';
end
$$;
SQL

printf '\nReconciliation staging verification\n'
printf '%s\n' '==================================='
docker exec -i "$db_container" \
  psql -X -v ON_ERROR_STOP=1 -U "$database_admin" -d "$canonical_database" \
  < "$repo_root/migration/supabase/sql/verify_reconciliation_loaded.sql"

printf '\nExact snapshot reconciliation completed without production writes.\n'
if [[ "$use_existing" == "1" ]]; then
  printf 'Existing disposable databases were left intact by explicit request.\n'
fi
