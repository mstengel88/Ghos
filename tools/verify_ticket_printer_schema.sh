#!/usr/bin/env bash
set -Eeuo pipefail

source_root="${TICKET_PRINTER_SOURCE_ROOT:-/Users/mattstengel/edit-my-ticket}"
migration_dir="$source_root/supabase/migrations"
candidate_migration="$(
  cd "$(dirname "${BASH_SOURCE[0]}")/.."
  pwd
)/migration/supabase/candidates/ticket-printer/000_live_dispatch_bridge.sql"
db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="ghos_ticket_printer_rehearsal_$$"
scheduler_migration="20260511173000_schedule_loadrite_sync_every_5_minutes.sql"

if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing verification for unexpected container: %s\n' \
    "$db_container" >&2
  exit 1
fi
if [[ ! -d "$migration_dir" ]]; then
  printf 'Ticket Printer migration directory not found: %s\n' \
    "$migration_dir" >&2
  exit 1
fi
if [[ ! -f "$candidate_migration" ]]; then
  printf 'Ticket Printer live-schema candidate not found: %s\n' \
    "$candidate_migration" >&2
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

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  >/dev/null <<'SQL'
create schema auth;
create schema extensions;

create table auth.users (
  id uuid primary key,
  email text,
  raw_user_meta_data jsonb not null default '{}'::jsonb
);

insert into auth.users (id, email)
values
  (
    '46c4375d-6d33-4962-9c77-c63fe1ba71d1',
    'rehearsal-admin-one@example.invalid'
  ),
  (
    'b9438690-e79b-4f4a-9d70-657c52e00588',
    'rehearsal-admin-two@example.invalid'
  ),
  (
    '48adee4d-50d0-4d88-be9e-f3930ba898db',
    'rehearsal-user@example.invalid'
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
SQL

migration_count=0
infrastructure_migration_count=0
for migration in "$migration_dir"/*.sql; do
  [[ -f "$migration" ]] || continue
  if [[ "$(basename "$migration")" == "$scheduler_migration" ]]; then
    printf 'Deferring infrastructure scheduler %s\n' \
      "$scheduler_migration"
    infrastructure_migration_count=$((infrastructure_migration_count + 1))
    continue
  fi
  docker exec -i "$db_container" \
    psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
    <"$migration" >/dev/null
  migration_count=$((migration_count + 1))
done

if [[ "$migration_count" -ne 38 || "$infrastructure_migration_count" -ne 1 ]]; then
  printf 'Unexpected migration split: %s schema, %s infrastructure.\n' \
    "$migration_count" "$infrastructure_migration_count" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" \
  <"$candidate_migration" >/dev/null

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

if [[ "$contract" != "14|14|53|10|7" ]]; then
  printf 'Unexpected Ticket Printer schema contract: %s\n' \
    "$contract" >&2
  exit 1
fi

printf '%s\n' \
  'Ticket Printer PostgreSQL 17 schema verification passed.' \
  'Contract: 14 tables, 14 RLS tables, 53 policies, 10 functions, 7 triggers.' \
  'The two empty live dispatch-bridge tables are supplied by a local candidate.' \
  'The Supabase pg_cron migration is intentionally deferred to GHOS scheduling.'
