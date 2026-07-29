#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${WINTERWATCH_SOURCE_ROOT:-/Users/mattstengel/winterwatch}"
migration_dir="$source_root/supabase/migrations"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="ghos_winterwatch_rehearsal_$$"
extension_migration="20260125154800_4e37349c-1021-4569-83d0-af0f53d92709.sql"
scheduler_migration="20260125154836_21b24871-a1ee-4480-9838-c268fdd4bd71.sql"
live_contract="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)/migration/supabase/baselines/winterwatch/900_live_contract.sql"

if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing verification for unexpected container: %s\n' \
    "$db_container" >&2
  exit 1
fi
if [[ ! -d "$migration_dir" ]]; then
  printf 'WinterWatch migration directory not found: %s\n' \
    "$migration_dir" >&2
  exit 1
fi
if [[ ! -f "$live_contract" ]]; then
  printf 'WinterWatch live reconciliation migration not found: %s\n' \
    "$live_contract" >&2
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
  docker exec "$db_container" \
    dropdb -U postgres --if-exists "$database_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker exec "$db_container" \
  createdb -U postgres -T template0 "$database_name"

# The application migrations expect the Supabase Auth and Storage schemas.
# These deliberately minimal compatibility objects let the schema chain run
# without copying any production identities, sessions, or files into the lab.
docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  >/dev/null <<'SQL'
create schema extensions;
create extension if not exists pgcrypto with schema extensions;

create schema auth;
create table auth.users (
  id uuid primary key,
  email text,
  raw_user_meta_data jsonb not null default '{}'::jsonb
);
create or replace function auth.uid()
returns uuid
language sql
stable
as $$
  select nullif(
    current_setting('request.jwt.claim.sub', true),
    ''
  )::uuid
$$;

create schema storage;
create table storage.buckets (
  id text primary key,
  name text not null unique,
  public boolean not null default false,
  file_size_limit bigint,
  allowed_mime_types text[]
);
create table storage.objects (
  id uuid primary key default gen_random_uuid(),
  bucket_id text not null references storage.buckets(id),
  name text not null
);
alter table storage.objects enable row level security;
create or replace function storage.foldername(name text)
returns text[]
language sql
immutable
as $$
  select (string_to_array(name, '/'))[1:-1]
$$;
SQL

migration_count=0
infrastructure_migration_count=0
for migration in "$migration_dir"/*.sql; do
  [[ -f "$migration" ]] || continue
  migration_name="$(basename "$migration")"
  if [[ "$migration_name" == "$extension_migration" ||
        "$migration_name" == "$scheduler_migration" ]]; then
    printf 'Deferring managed scheduler migration %s\n' "$migration_name"
    infrastructure_migration_count=$((infrastructure_migration_count + 1))
    continue
  fi

  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
    <"$migration" >/dev/null
  migration_count=$((migration_count + 1))
done

if [[ "$migration_count" -ne 34 ||
      "$infrastructure_migration_count" -ne 2 ]]; then
  printf 'Unexpected migration split: %s schema, %s infrastructure.\n' \
    "$migration_count" "$infrastructure_migration_count" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  <"$live_contract" >/dev/null

contract="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select
       (select count(*) from information_schema.tables
        where table_schema = 'public' and table_type = 'BASE TABLE'),
       (select count(*) from pg_class c join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'public' and c.relkind = 'r' and c.relrowsecurity),
       (select count(*) from pg_policies where schemaname = 'public'),
       (select count(*) from pg_proc p join pg_namespace n on n.oid = p.pronamespace
        where n.nspname = 'public'),
       (select count(*) from pg_trigger t join pg_class c on c.oid = t.tgrelid
        join pg_namespace n on n.oid = c.relnamespace
        where n.nspname = 'public' and not t.tgisinternal);"
)"

expected_contract="20|20|74|11|19"
if [[ "$contract" != "$expected_contract" ]]; then
  printf 'WinterWatch contract mismatch: expected %s, got %s.\n' \
    "$expected_contract" "$contract" >&2
  exit 1
fi

storage_contract="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select concat_ws('|',
       coalesce((select id from storage.buckets
                  where id = 'work-photos'), ''),
       coalesce((select public::text from storage.buckets
                  where id = 'work-photos'), ''),
       coalesce((select file_size_limit::text from storage.buckets
                  where id = 'work-photos'), ''),
       coalesce((select array_to_string(allowed_mime_types, ',')
                  from storage.buckets
                 where id = 'work-photos'), ''),
       (select count(*)::text
          from pg_policies
         where schemaname = 'storage'));"
)"

expected_storage_contract="work-photos|false|||6"
if [[ "$storage_contract" != "$expected_storage_contract" ]]; then
  printf 'WinterWatch Storage contract mismatch: expected %s, got %s.\n' \
    "$expected_storage_contract" "$storage_contract" >&2
  exit 1
fi

table_names="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select string_agg(table_name, ',' order by table_name)
       from information_schema.tables
      where table_schema = 'public'
        and table_type = 'BASE TABLE';"
)"

function_names="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select string_agg(
       p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')',
       ',' order by p.proname, pg_get_function_identity_arguments(p.oid))
       from pg_proc p
       join pg_namespace n on n.oid = p.pronamespace
      where n.nspname = 'public';"
)"

trigger_names="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select string_agg(c.relname || '.' || t.tgname, ','
                       order by c.relname, t.tgname)
       from pg_trigger t
       join pg_class c on c.oid = t.tgrelid
       join pg_namespace n on n.oid = c.relnamespace
      where n.nspname = 'public'
        and not t.tgisinternal;"
)"

fingerprints="$(
  docker exec "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atc \
    "select concat_ws('|',
       (select md5(coalesce(string_agg(
          table_name || '|' || ordinal_position::text || '|' ||
          column_name || '|' || data_type || '|' ||
          coalesce(udt_schema, '') || '|' || coalesce(udt_name, '') || '|' ||
          is_nullable || '|' || coalesce(column_default, '') || '|' ||
          coalesce(identity_generation, '') || '|' ||
          coalesce(is_generated, ''),
          E'\n' order by table_name, ordinal_position), ''))
          from information_schema.columns
         where table_schema = 'public'),
       (select md5(coalesce(string_agg(
          c.relname::text || '|' || con.conname::text || '|' ||
          con.contype::text || '|' || pg_get_constraintdef(con.oid, true),
          E'\n' order by c.relname, con.conname), ''))
          from pg_constraint con
          join pg_class c on c.oid = con.conrelid
          join pg_namespace n on n.oid = c.relnamespace
         where n.nspname = 'public'),
       (select md5(coalesce(string_agg(
          tablename || '|' || indexname || '|' || indexdef,
          E'\n' order by tablename, indexname), ''))
          from pg_indexes
         where schemaname = 'public'),
       (select md5(coalesce(string_agg(
          p.proname::text || '|' ||
          pg_get_function_identity_arguments(p.oid) || '|' ||
          pg_get_functiondef(p.oid),
          E'\n' order by p.proname,
          pg_get_function_identity_arguments(p.oid)), ''))
          from pg_proc p
          join pg_namespace n on n.oid = p.pronamespace
         where n.nspname = 'public'),
       (select md5(coalesce(string_agg(
          c.relname::text || '|' || t.tgname::text || '|' ||
          pg_get_triggerdef(t.oid, true),
          E'\n' order by c.relname, t.tgname), ''))
          from pg_trigger t
          join pg_class c on c.oid = t.tgrelid
          join pg_namespace n on n.oid = c.relnamespace
         where n.nspname = 'public'
           and not t.tgisinternal),
       (select md5(coalesce(string_agg(
          tablename || '|' || policyname || '|' || permissive || '|' ||
          array_to_string(roles, ',') || '|' || cmd || '|' ||
          coalesce(qual, '') || '|' || coalesce(with_check, ''),
          E'\n' order by tablename, policyname), ''))
          from pg_policies
         where schemaname = 'public'),
       (select md5(coalesce(string_agg(
          t.typname || '|' || array_to_string(x.labels, ','),
          E'\n' order by t.typname), ''))
          from pg_type t
          join pg_namespace n on n.oid = t.typnamespace
          cross join lateral (
            select array_agg(e.enumlabel order by e.enumsortorder) as labels
              from pg_enum e
             where e.enumtypid = t.oid
         ) x
         where n.nspname = 'public'
           and t.typtype = 'e'),
       (select md5(coalesce(string_agg(
          schemaname || '|' || tablename || '|' || policyname || '|' ||
          permissive || '|' || array_to_string(roles, ',') || '|' || cmd ||
          '|' || coalesce(qual, '') || '|' || coalesce(with_check, ''),
          E'\n' order by tablename, policyname), ''))
          from pg_policies
         where schemaname = 'storage'));"
)"

expected_fingerprints="af255063b9bcca0dbb09068bbdd40cce|ffdc7efa258adff6e793410c893abe29|b0468a2f6b2809b48f5f8a974ae18413|7c498acbfa9b299df7f0d57c98521e3a|6d94e340996d83d8fdfe360dba202aae|82388cf87708f568986c16daece3d9c3|817be365087d37cb6289998376694943|a0937b9aabadcb006da6f84f04d1c8d9"
if [[ "$fingerprints" != "$expected_fingerprints" ]]; then
  printf 'WinterWatch fingerprint mismatch.\nExpected: %s\nActual:   %s\n' \
    "$expected_fingerprints" "$fingerprints" >&2
  exit 1
fi

printf '%s\n' \
  'WinterWatch PostgreSQL 17 schema verification passed.' \
  "Contract: $contract (tables|RLS tables|policies|functions|triggers)." \
  "Storage contract: $storage_contract (bucket|public|size limit|MIME types|policies)." \
  "Fingerprints: $fingerprints (columns|constraints|indexes|functions|triggers|policies|enums|Storage policies)." \
  "Tables: $table_names" \
  "Functions: $function_names" \
  "Triggers: $trigger_names" \
  'The managed pg_cron/pg_net scheduler is intentionally deferred.'
