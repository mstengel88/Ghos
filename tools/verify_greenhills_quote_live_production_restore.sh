#!/usr/bin/env bash
set -Eeuo pipefail

db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"
database_name="${GREENHILLS_QUOTE_LIVE_REHEARSAL_DATABASE:-}"
expected_counts_file="${GREENHILLS_QUOTE_LIVE_EXPECTED_COUNTS_FILE:-}"

if [[ -z "$database_name" ]]; then
  printf '%s\n' \
    'Set GREENHILLS_QUOTE_LIVE_REHEARSAL_DATABASE to the isolated restored database.' >&2
  exit 1
fi
if [[ ! "$database_name" =~ ^[a-z_][a-z0-9_]*$ ]]; then
  printf 'Unsafe database name: %s\n' "$database_name" >&2
  exit 1
fi
if [[ ! -s "$expected_counts_file" ]]; then
  printf 'Expected-count manifest is missing or empty: %s\n' \
    "$expected_counts_file" >&2
  exit 1
fi
if [[ "$db_container" != "supabase-db" ]]; then
  printf 'Refusing unexpected database container: %s\n' "$db_container" >&2
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
  if total_count <> 22 or valid_count <> 22 then
    raise exception
      'RLS verification failed: % of % public tables enabled; expected 22 of 22',
      valid_count,
      total_count;
  end if;

  select count(*) into total_count
  from pg_policies
  where schemaname = 'public';
  if total_count <> 20 then
    raise exception 'Policy count mismatch: expected 20, found %', total_count;
  end if;

  select count(*) into total_count
  from pg_proc p
  join pg_namespace n on n.oid = p.pronamespace
  where n.nspname = 'public';
  if total_count <> 8 then
    raise exception 'Function count mismatch: expected 8, found %', total_count;
  end if;

  select count(*) into total_count
  from pg_trigger t
  join pg_class c on c.oid = t.tgrelid
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and not t.tgisinternal;
  if total_count <> 8 then
    raise exception 'Trigger count mismatch: expected 8, found %', total_count;
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
  from public.app_user_profiles p
  left join auth.users u on u.id = p.id
  where u.id is null;
  if total_count <> 0 then
    raise exception 'Found % orphan app user profiles', total_count;
  end if;

  select count(*) into total_count
  from public.dispatch_user_roles r
  left join auth.users u on u.id = r.user_id
  where u.id is null;
  if total_count <> 0 then
    raise exception 'Found % orphan dispatch user roles', total_count;
  end if;

  select count(*) into total_count
  from storage.objects o
  left join storage.buckets b on b.id = o.bucket_id
  where b.id is null;
  if total_count <> 0 then
    raise exception 'Found % Storage objects without a bucket', total_count;
  end if;
end
$$;

select
  (select count(*) from auth.users) as auth_users,
  (select count(*) from auth.identities) as auth_identities,
  (select count(*) from public.app_user_profiles) as app_profiles,
  (select count(*) from public.dispatch_user_roles) as dispatch_roles,
  (select count(*) from public.dispatch_orders) as orders,
  (select count(*) from public.custom_delivery_quotes) as quotes,
  (select count(*) from storage.objects) as storage_objects,
  (
    select count(*)
    from pg_policies
    where schemaname = 'public'
  ) as public_policies;
SQL

while IFS='|' read -r relation expected_count; do
  if [[ ! "$relation" =~ ^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$ ]] ||
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

printf 'GreenHills Quote Live production restore verification passed for %s.\n' \
  "$database_name"
