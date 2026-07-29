#!/usr/bin/env bash
set -Eeuo pipefail

db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="${WINTERWATCH_REHEARSAL_DATABASE:-}"
expected_counts_file="${WINTERWATCH_EXPECTED_COUNTS_FILE:-}"

if [[ -z "$database_name" ]]; then
  printf '%s\n' \
    'Set WINTERWATCH_REHEARSAL_DATABASE to the isolated restored database.' >&2
  exit 1
fi
if [[ ! "$database_name" =~ ^[a-z_][a-z0-9_]*$ ]]; then
  printf 'Unsafe database name: %s\n' "$database_name" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -P pager=off \
    -U postgres -d "$database_name" <<'SQL'
do $$
declare
  total_count bigint;
  valid_count bigint;
begin
  select count(*), count(*) filter (where c.relrowsecurity)
  into total_count, valid_count
  from pg_class c
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and c.relkind in ('r', 'p');
  if total_count <> 20 or valid_count <> 20 then
    raise exception
      'RLS verification failed: % of % public tables enabled; expected 20 of 20',
      valid_count,
      total_count;
  end if;

  select count(*) into total_count
  from pg_policies
  where schemaname = 'public';
  if total_count <> 74 then
    raise exception 'Policy count mismatch: expected 74, found %', total_count;
  end if;

  select count(*) into total_count
  from pg_index
  where not indisvalid or not indisready;
  if total_count <> 0 then
    raise exception 'Found % invalid or unready indexes', total_count;
  end if;

  select count(*) into total_count
  from pg_constraint
  where connamespace in (
    'public'::regnamespace,
    'auth'::regnamespace,
    'storage'::regnamespace
  )
  and not convalidated;
  if total_count <> 0 then
    raise exception 'Found % unvalidated constraints', total_count;
  end if;

  select count(*) into total_count
  from auth.identities i
  left join auth.users u on u.id = i.user_id
  where u.id is null;
  if total_count <> 0 then
    raise exception 'Found % orphan Auth identities', total_count;
  end if;

  select count(*) into total_count
  from public.profiles p
  left join auth.users u on u.id = p.id
  where u.id is null;
  if total_count <> 0 then
    raise exception 'Found % orphan profiles', total_count;
  end if;
end
$$;

select
  (select count(*) from auth.users) as auth_users,
  (select count(*) from auth.identities) as auth_identities,
  (select count(*) from public.accounts) as accounts,
  (select count(*) from public.audit_logs) as audit_logs,
  (select count(*) from storage.objects) as storage_objects,
  (
    select count(*)
    from pg_policies
    where schemaname = 'public'
  ) as public_policies;
SQL

if [[ -z "$expected_counts_file" ]]; then
  expected_counts_file="$(mktemp "${TMPDIR:-/tmp}/winterwatch-expected-counts.XXXXXX")"
  trap 'rm -f "$expected_counts_file"' EXIT
  cat > "$expected_counts_file" <<'EOF'
auth.identities|13
auth.users|12
public.accounts|24
public.audit_logs|840
public.employee_locations|3
public.employees|15
public.equipment|14
public.maintenance_logs|1
public.maintenance_notification_settings|5
public.maintenance_requests|4
public.notification_preferences|1
public.notification_types|4
public.notifications_log|183
public.overtime_notification_settings|2
public.overtime_notifications_sent|26
public.profiles|12
public.push_device_tokens|8
public.scheduled_notifications|0
public.shovel_work_logs|57
public.time_clock|38
public.user_roles|15
public.work_logs|166
storage.buckets|1
storage.objects|92
EOF
fi

if [[ ! -s "$expected_counts_file" ]]; then
  printf 'Expected-count manifest is missing or empty: %s\n' \
    "$expected_counts_file" >&2
  exit 1
fi

while IFS='|' read -r relation expected_count; do
  if [[ ! "$relation" =~ ^[a-z_][a-z0-9_]*\.[a-z_][a-z0-9_]*$ ]] ||
    [[ ! "$expected_count" =~ ^[0-9]+$ ]]; then
    printf 'Invalid expected-count row: %s|%s\n' \
      "$relation" "$expected_count" >&2
    exit 1
  fi

  schema_name="${relation%%.*}"
  table_name="${relation#*.}"
  actual_count="$(
    docker exec "$db_container" \
      psql -v ON_ERROR_STOP=1 -U postgres -d "$database_name" -Atqc \
        "select count(*) from \"$schema_name\".\"$table_name\""
  )"
  if [[ "$actual_count" != "$expected_count" ]]; then
    printf 'Count mismatch for %s: expected %s, found %s.\n' \
      "$relation" "$expected_count" "$actual_count" >&2
    exit 1
  fi
done < "$expected_counts_file"

printf 'WinterWatch restore verification passed for %s.\n' "$database_name"
