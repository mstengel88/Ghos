#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
db_container="${OPERATIONS_DB_CONTAINER:-ghos-operations-db}"
database_name="operations_restore_$(date -u +%Y%m%d_%H%M%S)"
dump_migrations="${DUMP_SITE_MIGRATIONS:-/Users/mattstengel/Documents/GreenHills APP/supabase/migrations}"
work_root="$(mktemp -d "${TMPDIR:-/tmp}/ghos-operations-restore.XXXXXX")"
ticket_root="$work_root/ticket"
dump_root="$work_root/dump"
ticket_archive=""
dump_archive=""
candidate_created=0
services_stopped=0
app_containers=(
  ghos-operations-api
  ghos-operations-auth
  ghos-operations-functions
  ghos-operations-rest
)

cleanup() {
  set +e
  if [[ "$candidate_created" == 1 ]]; then
    docker exec "$db_container" dropdb -U postgres --if-exists --force \
      "$database_name" >/dev/null 2>&1
  fi
  if [[ "$services_stopped" == 1 ]]; then
    docker start "${app_containers[@]}" >/dev/null 2>&1
  fi
  rm -rf "$work_root"
}
trap cleanup EXIT

for command_name in docker openssl python3 security shasum tar; do
  command -v "$command_name" >/dev/null 2>&1 || {
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 1
  }
done

[[ "$db_container" == "ghos-operations-db" ]] || {
  printf 'Refusing unexpected Operations database container: %s\n' \
    "$db_container" >&2
  exit 1
}
[[ -d "$dump_migrations" ]] || {
  printf 'Dump Site migrations not found: %s\n' "$dump_migrations" >&2
  exit 1
}

container_status="$(docker inspect --format \
  '{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}' \
  "$db_container")"
[[ "$container_status" == "running|healthy" ]] || {
  printf 'Operations database is not healthy: %s\n' "$container_status" >&2
  exit 1
}

latest_archive() {
  local slug="$1"
  find "$repo_root/migration/supabase/exports/$slug" \
    -type f -name "$slug-database.sql.tar.gz.enc" -print 2>/dev/null |
    sort | tail -n 1
}

ticket_archive="$(latest_archive ticket-printer)"
dump_archive="$(latest_archive dump-site)"
for archive in "$ticket_archive" "$dump_archive"; do
  [[ -n "$archive" && -s "$archive" ]] || {
    printf 'A required encrypted Operations source archive is missing.\n' >&2
    exit 1
  }
  if [[ -s "$archive.sha256" ]]; then
    (cd "$(dirname "$archive")" && \
      shasum -a 256 -c "$(basename "$archive").sha256")
  fi
done

decrypt_archive() {
  local archive="$1"
  local account="$2"
  local destination="$3"
  local password
  local plaintext="$work_root/$account.tar.gz"

  password="$(security find-generic-password \
    -s 'GHOS Migration Export Encryption' -a "$account" -w)"
  GHOS_EXPORT_PASSWORD="$password" openssl enc -d -aes-256-cbc \
    -pbkdf2 -iter 250000 -md sha256 -pass env:GHOS_EXPORT_PASSWORD \
    -in "$archive" -out "$plaintext"
  unset password
  mkdir -p "$destination"
  tar -xzf "$plaintext" -C "$destination"
  (cd "$destination" && shasum -a 256 -c SHA256SUMS)
}

decrypt_archive "$ticket_archive" ticket-printer "$ticket_root"
decrypt_archive "$dump_archive" dump-site "$dump_root"

python3 - "$ticket_root/data.sql" "$work_root/ticket-data.sql" <<'PY'
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text(encoding="utf-8").splitlines(keepends=True)
target = Path(sys.argv[2])
output = []
inside_copy = False
skip_copy = False
custom_oauth = False
custom_oauth_rows = 0

for line in source:
    if line.startswith('COPY '):
        inside_copy = True
        skip_copy = line.startswith('COPY "storage".')
        custom_oauth = line.startswith('COPY "auth"."custom_oauth_providers" ')
        if custom_oauth:
            fragment = ', "custom_claims_allowlist"'
            if fragment not in line:
                raise SystemExit("Expected Auth compatibility column is missing.")
            line = line.replace(fragment, "", 1)
    elif inside_copy and line.rstrip("\n") == r"\.":
        if not skip_copy:
            output.append(line)
        inside_copy = False
        skip_copy = False
        custom_oauth = False
        continue
    elif inside_copy and custom_oauth:
        custom_oauth_rows += 1

    if not skip_copy:
        output.append(line)

if custom_oauth_rows:
    raise SystemExit(
        "Refusing Auth compatibility rewrite because custom OAuth rows exist."
    )
target.write_text("".join(output), encoding="utf-8")
PY

python3 - "$dump_root/data.sql" "$work_root/dump-application-data.sql" <<'PY'
import re
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text(encoding="utf-8").splitlines(keepends=True)
target = Path(sys.argv[2])
tables = {
    "dump_site_entries",
    "dump_site_rate_limits",
    "dump_site_sessions",
}
sequences = {
    "dump_site_201_d_order_number_seq",
    "dump_site_order_number_seq",
}
output = []
inside_copy = False
keep_copy = False
seen_tables = set()
seen_sequences = set()

for line in source:
    match = re.match(r'^COPY "public"\."([^"]+)" ', line)
    if match:
        inside_copy = True
        keep_copy = match.group(1) in tables
        if keep_copy:
            seen_tables.add(match.group(1))
            output.append(line)
        continue
    if inside_copy:
        if keep_copy:
            output.append(line)
        if line.rstrip("\n") == r"\.":
            inside_copy = False
            keep_copy = False
        continue

    if line.startswith("SELECT pg_catalog.setval("):
        for sequence in sequences:
            if f'"public"."{sequence}"' in line:
                seen_sequences.add(sequence)
                output.append(line)
                break

if seen_tables != tables:
    raise SystemExit(f"Missing Dump Site COPY blocks: {sorted(tables-seen_tables)}")
if seen_sequences != sequences:
    raise SystemExit(
        f"Missing Dump Site sequence values: {sorted(sequences-seen_sequences)}"
    )
target.write_text("".join(output), encoding="utf-8")
PY

sed \
  's/^CREATE EXTENSION IF NOT EXISTS "pg_cron"/-- Operations rehearsal skips pg_cron: CREATE EXTENSION IF NOT EXISTS "pg_cron"/' \
  "$ticket_root/schema.sql" > "$work_root/ticket-schema.sql"

docker stop -t 30 "${app_containers[@]}" >/dev/null
services_stopped=1
docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d template1 <<SQL
select pg_terminate_backend(pid)
from pg_stat_activity
where datname = 'postgres' and pid <> pg_backend_pid();
create database $database_name with template postgres owner postgres;
SQL
candidate_created=1
docker start "${app_containers[@]}" >/dev/null
services_stopped=0

docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" <<'SQL'
drop schema public cascade;
create schema public authorization postgres;
grant usage on schema public to public;
grant create on schema public to public;

-- Supabase's platform image normally creates this before installing
-- supabase_vault. A database cloned from our lightweight Operations template
-- has the extension binary available but not the destination schema.
create schema if not exists vault authorization supabase_admin;

do $$
declare target record;
begin
  for target in
    select n.nspname, c.relname
    from pg_class c
    join pg_namespace n on n.oid = c.relnamespace
    where c.relkind in ('r', 'p')
      and n.nspname = 'auth'
      and c.relname <> 'schema_migrations'
  loop
    execute format('truncate table %I.%I restart identity cascade',
      target.nspname, target.relname);
  end loop;
end
$$;
SQL

printf 'Restoring Ticket Printer schema and production data...\n'
docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" < "$work_root/ticket-schema.sql"
docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" < "$work_root/ticket-data.sql"

printf 'Applying the eight reviewed Dump Site migrations...\n'
dump_migration_count=0
while IFS= read -r migration; do
  docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
    -U postgres -d "$database_name" < "$migration" >/dev/null
  dump_migration_count=$((dump_migration_count + 1))
done < <(find "$dump_migrations" -maxdepth 1 -type f \
  -name '*dump_site*.sql' -print | sort)
[[ "$dump_migration_count" == 8 ]] || {
  printf 'Expected 8 Dump Site migrations; applied %s.\n' \
    "$dump_migration_count" >&2
  exit 1
}

printf 'Importing only Dump Site application rows and sequence positions...\n'
docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" < "$work_root/dump-application-data.sql"

actual_contract="$(docker exec "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" -Atqc \
  "select
    (select count(*) from information_schema.tables
      where table_schema='public' and table_type='BASE TABLE'),
    (select count(*) from pg_class c join pg_namespace n on n.oid=c.relnamespace
      where n.nspname='public' and c.relkind='r' and c.relrowsecurity),
    (select count(*) from pg_policies where schemaname='public'),
    (select count(*) from pg_proc p join pg_namespace n on n.oid=p.pronamespace
      where n.nspname='public'),
    (select count(*) from pg_trigger t join pg_class c on c.oid=t.tgrelid
      join pg_namespace n on n.oid=c.relnamespace
      where n.nspname='public' and not t.tgisinternal);")"

[[ "$actual_contract" == "18|18|46|14|10" ]] || {
  printf 'Unexpected combined Operations contract: %s\n' \
    "$actual_contract" >&2
  exit 1
}

actual_counts="$(docker exec -i "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" -At -F '|' <<'SQL'
select 'auth.identities', count(*) from auth.identities
union all select 'auth.users', count(*) from auth.users
union all select 'public.agent_registry', count(*) from public.agent_registry
union all select 'public.audit_logs', count(*) from public.audit_logs
union all select 'public.customers', count(*) from public.customers
union all select 'public.dispatch_orders', count(*) from public.dispatch_orders
union all select 'public.dispatch_routes', count(*) from public.dispatch_routes
union all select 'public.dump_site_entries', count(*) from public.dump_site_entries
union all select 'public.dump_site_rate_limits', count(*) from public.dump_site_rate_limits
union all select 'public.dump_site_sessions', count(*) from public.dump_site_sessions
union all select 'public.feedback', count(*) from public.feedback
union all select 'public.orders', count(*) from public.orders
union all select 'public.products', count(*) from public.products
union all select 'public.profiles', count(*) from public.profiles
union all select 'public.template_versions', count(*) from public.template_versions
union all select 'public.ticket_templates', count(*) from public.ticket_templates
union all select 'public.tickets', count(*) from public.tickets
union all select 'public.trucks', count(*) from public.trucks
union all select 'public.user_roles', count(*) from public.user_roles
union all select 'sequence.dump_site_201_d_order_number_seq', last_value from public.dump_site_201_d_order_number_seq
union all select 'sequence.dump_site_order_number_seq', last_value from public.dump_site_order_number_seq
order by 1;
SQL
)"

python3 - "$ticket_root/expected-counts.tsv" \
  "$dump_root/expected-counts.tsv" "$actual_counts" <<'PY'
import sys
from pathlib import Path

expected = {}
for path in (Path(sys.argv[1]), Path(sys.argv[2])):
    for line in path.read_text(encoding="utf-8").splitlines():
        relation, value = line.split("|", 1)
        if relation.startswith("storage."):
            if int(value) != 0:
                raise SystemExit("Operations stack omits Storage but source is non-empty.")
            continue
        if relation.startswith("auth.") and relation in expected:
            if int(value) != 0:
                raise SystemExit("Dump Site unexpectedly contains Auth records.")
            continue
        expected[relation] = int(value)

actual = {}
for line in sys.argv[3].splitlines():
    relation, value = line.split("|", 1)
    actual[relation] = int(value)

if expected != actual:
    missing = sorted(expected.keys() - actual.keys())
    extra = sorted(actual.keys() - expected.keys())
    changed = sorted(
        key for key in expected.keys() & actual.keys()
        if expected[key] != actual[key]
    )
    raise SystemExit(
        f"Operations count mismatch; missing={missing}, extra={extra}, changed="
        f"{[(key, expected[key], actual[key]) for key in changed]}"
    )
PY

exposure_count="$(docker exec "$db_container" psql -v ON_ERROR_STOP=1 \
  -U postgres -d "$database_name" -Atqc \
  "select count(*) from information_schema.role_table_grants
    where table_schema='public'
      and table_name like 'dump_site_%'
      and grantee in ('anon','authenticated');")"
[[ "$exposure_count" == 0 ]] || {
  printf 'Dump Site browser roles received %s table grant(s).\n' \
    "$exposure_count" >&2
  exit 1
}

printf '%s\n' \
  'GHOS Operations disposable restore rehearsal passed.' \
  "Contract: $actual_contract (tables|RLS tables|policies|functions|triggers)." \
  'Ticket Printer Auth and application counts match the encrypted source.' \
  'Dump Site table counts and both sequence positions match the encrypted source.' \
  'Dump Site remains service-only with no anon/authenticated table grants.'
