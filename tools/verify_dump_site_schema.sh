#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${DUMP_SITE_SOURCE_ROOT:-/Users/mattstengel/Documents/GreenHills APP}"
migration_dir="$source_root/supabase/migrations"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="ghos_dump_site_rehearsal_$$"

if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing verification for unexpected container: %s\n' \
    "$db_container" >&2
  exit 1
fi
if [[ ! -d "$migration_dir" ]]; then
  printf 'Dump Site migration directory not found: %s\n' \
    "$migration_dir" >&2
  exit 1
fi

container_status="$(
  docker inspect \
    --format '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' \
    "$db_container"
)"
if [[ "$container_status" != "running|healthy" ]]; then
  printf 'Local Supabase database is not healthy: %s\n' \
    "$container_status" >&2
  exit 1
fi

cleanup() {
  docker exec -i "$db_container" \
    dropdb -U postgres --if-exists "$database_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker exec "$db_container" \
  createdb -U postgres -T template0 "$database_name"

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  >/dev/null <<'SQL'
create schema if not exists extensions;
create extension if not exists pgcrypto with schema extensions;
SQL

migration_count=0
for migration in "$migration_dir"/*.sql; do
  [[ -f "$migration" ]] || continue
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
    <"$migration" >/dev/null
  migration_count=$((migration_count + 1))
done

if [[ "$migration_count" -ne 8 ]]; then
  printf 'Expected eight Dump Site migrations, applied %s.\n' \
    "$migration_count" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  >/dev/null <<'SQL'
do $verification$
declare
  table_count integer;
  rls_count integer;
  application_function_count integer;
  entry_column_count integer;
  new_entry_id uuid;
  confirmation text;
  queue_status text;
  claimed record;
  completed boolean;
  first_attempt integer;
  second_attempt integer;
begin
  select count(*)
  into table_count
  from information_schema.tables
  where table_schema = 'public'
    and table_type = 'BASE TABLE';

  if table_count <> 3 then
    raise exception 'Expected three public tables, found %', table_count;
  end if;

  select count(*)
  into rls_count
  from pg_class c
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and c.relkind = 'r'
    and c.relrowsecurity;

  if rls_count <> 3 then
    raise exception 'Expected RLS on three tables, found %', rls_count;
  end if;

  select count(*)
  into entry_column_count
  from information_schema.columns
  where table_schema = 'public'
    and table_name = 'dump_site_entries';

  if entry_column_count <> 32 then
    raise exception
      'Expected 32 dump_site_entries columns, found %',
      entry_column_count;
  end if;

  select count(*)
  into application_function_count
  from pg_proc p
  join pg_namespace n on n.oid = p.pronamespace
  where n.nspname = 'public'
    and p.proname in (
      'increment_dump_site_rate_limit',
      'assign_dump_site_order_number',
      'request_dump_site_counterpoint_bridge',
      'claim_dump_site_counterpoint_bridge',
      'complete_dump_site_counterpoint_bridge',
      'list_dump_site_counterpoint_operator',
      'claim_dump_site_counterpoint_operator',
      'release_dump_site_counterpoint_operator',
      'queue_disabled_modern_retail_dump_site_entry'
    );

  if application_function_count <> 9 then
    raise exception
      'Expected nine Dump Site functions, found %',
      application_function_count;
  end if;

  if has_table_privilege(
    'anon',
    'public.dump_site_entries',
    'select'
  ) then
    raise exception 'anon unexpectedly has dump-site entry access';
  end if;

  if has_table_privilege(
    'authenticated',
    'public.dump_site_entries',
    'select'
  ) then
    raise exception 'authenticated unexpectedly has dump-site entry access';
  end if;

  if not has_table_privilege(
    'service_role',
    'public.dump_site_entries',
    'select,insert,update,delete'
  ) then
    raise exception 'service_role is missing dump-site entry access';
  end if;

  insert into public.dump_site_entries (
    access_source,
    shopify_customer,
    shopify_company_id,
    company_name,
    truck_number,
    driver_name,
    material_type,
    vehicle_type,
    modern_retail_status
  )
  values (
    'qr',
    null,
    'rehearsal-company',
    'Rehearsal Company',
    'TEST-TRUCK',
    'Rehearsal Driver',
    'Clean Fill',
    'Pickup Truck',
    'disabled'
  )
  returning id, confirmation_id, counterpoint_bridge_status
  into new_entry_id, confirmation, queue_status;

  if confirmation <> '201-D10000' then
    raise exception
      'Expected first order number 201-D10000, found %',
      confirmation;
  end if;
  if queue_status <> 'queued' then
    raise exception
      'Disabled Modern Retail entry was not queued: %',
      queue_status;
  end if;

  select *
  into claimed
  from public.claim_dump_site_counterpoint_bridge(
    'schema-rehearsal',
    1,
    5,
    180
  );

  if claimed.id is distinct from new_entry_id then
    raise exception 'The queued rehearsal entry was not claimed';
  end if;

  select public.complete_dump_site_counterpoint_bridge(
    new_entry_id,
    'schema-rehearsal',
    claimed.claim_token,
    'created',
    'REHEARSAL-1',
    null
  )
  into completed;

  if not completed then
    raise exception 'The rehearsal bridge claim was not completed';
  end if;

  select public.increment_dump_site_rate_limit(
    'schema-rehearsal',
    now() + interval '10 minutes'
  )
  into first_attempt;
  select public.increment_dump_site_rate_limit(
    'schema-rehearsal',
    now() + interval '10 minutes'
  )
  into second_attempt;

  if first_attempt <> 1 or second_attempt <> 2 then
    raise exception
      'Rate limit counter returned unexpected values: %, %',
      first_attempt,
      second_attempt;
  end if;
end
$verification$;
SQL

schema_fingerprints="$(
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -At <<'SQL'
with columns_data as (
  select table_name, ordinal_position, column_name, data_type, udt_name,
         is_nullable, coalesce(column_default, '') as column_default,
         coalesce(generation_expression, '') as generation_expression
  from information_schema.columns
  where table_schema = 'public'
  order by table_name, ordinal_position
), constraints_data as (
  select c.relname as table_name, con.conname,
         pg_get_constraintdef(con.oid, true) as definition
  from pg_constraint con
  join pg_class c on c.oid = con.conrelid
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
  order by c.relname, con.conname
), indexes_data as (
  select tablename as table_name, indexname, indexdef
  from pg_indexes
  where schemaname = 'public'
  order by tablename, indexname
), functions_data as (
  select p.proname, pg_get_function_identity_arguments(p.oid) as arguments,
         pg_get_functiondef(p.oid) as definition
  from pg_proc p
  join pg_namespace n on n.oid = p.pronamespace
  where n.nspname = 'public'
  order by p.proname, arguments
), triggers_data as (
  select c.relname as table_name, t.tgname,
         pg_get_triggerdef(t.oid, true) as definition
  from pg_trigger t
  join pg_class c on c.oid = t.tgrelid
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public' and not t.tgisinternal
  order by c.relname, t.tgname
)
select concat_ws(
  '|',
  (select md5(string_agg(row_to_json(columns_data)::text, E'\n'))
   from columns_data),
  (select md5(string_agg(row_to_json(constraints_data)::text, E'\n'))
   from constraints_data),
  (select md5(string_agg(row_to_json(indexes_data)::text, E'\n'))
   from indexes_data),
  (select md5(string_agg(row_to_json(functions_data)::text, E'\n'))
   from functions_data),
  (select md5(string_agg(row_to_json(triggers_data)::text, E'\n'))
   from triggers_data)
);
SQL
)"

expected_fingerprints="$(
  printf '%s' \
    'f6464afaf39d2b4ec48629490de5edaa|' \
    '41001e8debab49bf744c5f5d1a641cdc|' \
    'e66b81388668af3f3f0606a34a797231|' \
    '90d1ea47e1df769bbb8520e09a447663|' \
    'f451626de7cb25eb6b0a4077e7b35aec'
)"

if [[ "$schema_fingerprints" != "$expected_fingerprints" ]]; then
  printf 'Dump Site schema differs from the 2026-07-28 live contract.\n' >&2
  printf 'Expected: %s\nActual:   %s\n' \
    "$expected_fingerprints" "$schema_fingerprints" >&2
  exit 1
fi

printf '%s\n' \
  'Dump Site PostgreSQL 17 schema and queue workflow verification passed.' \
  'The disposable schema matches all five 2026-07-28 live fingerprints.'
