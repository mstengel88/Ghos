#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${WINTERWATCH_SOURCE_ROOT:-/Users/mattstengel/winterwatch}"
migration_dir="$source_root/supabase/migrations"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="ghos_winterwatch_rehearsal_$$"
extension_migration="20260125154800_4e37349c-1021-4569-83d0-af0f53d92709.sql"
scheduler_migration="20260125154836_21b24871-a1ee-4480-9838-c268fdd4bd71.sql"

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
create extension if not exists pgcrypto;

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
  public boolean not null default false
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

printf '%s\n' \
  'WinterWatch PostgreSQL 17 schema verification passed.' \
  "Contract: $contract (tables|RLS tables|policies|functions|triggers)." \
  'The managed pg_cron/pg_net scheduler is intentionally deferred.'
