\pset pager off
\timing off

-- Schema-only acceptance contract. This returns no application rows,
-- identities, object names, credentials, or secret values.

select
  count(*) as public_table_count,
  count(*) filter (where c.relrowsecurity) as rls_enabled_table_count
from pg_class c
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and c.relkind = 'r';

select
  count(*) as public_policy_count
from pg_policies
where schemaname = 'public';

select
  count(*) as public_function_count
from pg_proc p
join pg_namespace n on n.oid = p.pronamespace
where n.nspname = 'public';

select
  count(*) as public_trigger_count
from pg_trigger t
join pg_class c on c.oid = t.tgrelid
join pg_namespace n on n.oid = c.relnamespace
where n.nspname = 'public'
  and not t.tgisinternal;

select
  count(*) as public_index_count
from pg_indexes
where schemaname = 'public';

select
  table_name,
  count(*) as column_count,
  md5(
    string_agg(
      concat_ws(
        '|',
        column_name,
        data_type,
        udt_name,
        is_nullable,
        coalesce(column_default, '[null]')
      ),
      E'\n'
      order by column_name
    )
  ) as semantic_column_hash
from information_schema.columns
where table_schema = 'public'
group by table_name
order by table_name;

select
  c.conrelid::regclass::text as table_name,
  c.conname,
  c.contype,
  pg_get_constraintdef(c.oid, true) as definition
from pg_constraint c
join pg_namespace n on n.oid = c.connamespace
where n.nspname = 'public'
order by table_name, c.conname;

select
  id,
  public,
  file_size_limit,
  allowed_mime_types
from storage.buckets
where id = 'dispatch-photos';

select
  pubname,
  schemaname,
  tablename
from pg_publication_tables
where pubname = 'supabase_realtime'
  and schemaname = 'public'
order by tablename;

do $$
declare
  actual_count bigint;
  actual_hash text;
  schema_mismatches text;
begin
  select count(*) into actual_count
  from pg_class c
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and c.relkind = 'r';
  if actual_count <> 22 then
    raise exception 'Expected 22 public tables, found %', actual_count;
  end if;

  select count(*) into actual_count
  from pg_class c
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and c.relkind = 'r'
    and c.relrowsecurity;
  if actual_count <> 22 then
    raise exception 'Expected RLS on 22 public tables, found %', actual_count;
  end if;

  select count(*) into actual_count
  from pg_policies
  where schemaname = 'public';
  if actual_count <> 26 then
    raise exception 'Expected 26 public policies, found %', actual_count;
  end if;

  select count(*) into actual_count
  from pg_proc p
  join pg_namespace n on n.oid = p.pronamespace
  where n.nspname = 'public';
  if actual_count <> 8 then
    raise exception 'Expected 8 public functions, found %', actual_count;
  end if;

  select count(*) into actual_count
  from pg_trigger t
  join pg_class c on c.oid = t.tgrelid
  join pg_namespace n on n.oid = c.relnamespace
  where n.nspname = 'public'
    and not t.tgisinternal;
  if actual_count <> 8 then
    raise exception 'Expected 8 public triggers, found %', actual_count;
  end if;

  select count(*) into actual_count
  from pg_indexes
  where schemaname = 'public';
  if actual_count <> 106 then
    raise exception 'Expected 106 public indexes, found %', actual_count;
  end if;

  with expected(table_name, column_count, semantic_hash) as (
    values
      ('Session', 17, '0738c3d4eaaed3dc11498140881cb59b'),
      ('app_audit_log', 10, '3630b98df62ac9732d83b93183349b78'),
      ('app_settings', 12, 'e37986579ea68ef0878b7257b95de903'),
      ('app_user_profiles', 9, '9777295ed8b8c25447b683cd63da4e6d'),
      ('custom_delivery_quotes', 38, 'f2abe677e9709dbe35205603356dfe9b'),
      ('dispatch_audit_log', 9, 'da15e530a3ea6e340d58027abdc9b333'),
      ('dispatch_b2b_companies', 23, 'a7ef8275e7f88a6245307d47f8aae4e8'),
      ('dispatch_driver_locations', 15, '6851518fc4659f432236a5750759ec6d'),
      ('dispatch_employees', 11, 'bb71b9b05b977e017eac66848cf7c87c'),
      ('dispatch_notifications', 12, 'd58c96f0054cdfa44f49b533129146a0'),
      ('dispatch_orders', 38, 'dc3d46a93e8100542b986168c04a4090'),
      ('dispatch_push_subscriptions', 10, 'e6e37e234d7707882ef23411c5cb53a0'),
      ('dispatch_routes', 14, '54962f0c0dce9c86f863a14d816dba22'),
      ('dispatch_settings', 3, '14be639f31c435202819a5fd98cb5b52'),
      ('dispatch_shopify_updates', 10, 'ea6480d04e3fccd982e8512d52169e82'),
      ('dispatch_stop_metrics', 23, '980d36d13e30b79cdf19433d65898b91'),
      ('dispatch_trucks', 13, 'f379fef3965704826dd76ba02912caf9'),
      ('dispatch_user_roles', 8, '8be6433d84ef851b998999093a8d8544'),
      ('origin_addresses', 6, '51dc95262d21620009acc9b8ab79d396'),
      ('product_source_map', 12, '2262d53642604e2958ada9b371147520'),
      ('shipping_material_rules', 7, '547e972be8ad652fcfbdc4576ee4fce2'),
      ('shopify_app_settings', 8, '8fdf72d278699a97bf16b85d1326658e')
  ),
  actual as (
    select
      table_name,
      count(*)::integer as column_count,
      md5(
        string_agg(
          concat_ws(
            '|',
            column_name,
            data_type,
            udt_name,
            is_nullable,
            coalesce(column_default, '[null]')
          ),
          E'\n'
          order by column_name
        )
      ) as semantic_hash
    from information_schema.columns
    where table_schema = 'public'
    group by table_name
  )
  select string_agg(
    coalesce(expected.table_name, actual.table_name),
    ', '
    order by coalesce(expected.table_name, actual.table_name)
  )
  into schema_mismatches
  from expected
  full join actual using (table_name)
  where expected.table_name is null
     or actual.table_name is null
     or expected.column_count <> actual.column_count
     or expected.semantic_hash <> actual.semantic_hash;

  if schema_mismatches is not null then
    raise exception 'Column contract mismatch: %', schema_mismatches;
  end if;

  select
    count(*),
    md5(
      string_agg(
        concat_ws(
          '|',
          c.conrelid::regclass::text,
          c.conname,
          c.contype,
          pg_get_constraintdef(c.oid, true)
        ),
        E'\n'
        order by c.conrelid::regclass::text, c.conname
      )
    )
  into actual_count, actual_hash
  from pg_constraint c
  join pg_namespace n on n.oid = c.connamespace
  where n.nspname = 'public';

  if actual_count <> 45
     or actual_hash <> '0eac4add1988bf3f795f78b785f98e1b' then
    raise exception
      'Constraint contract mismatch: count %, hash %',
      actual_count,
      actual_hash;
  end if;

  if not exists (
    select 1
    from storage.buckets
    where id = 'dispatch-photos'
      and public = true
      and file_size_limit = 10485760
      and allowed_mime_types = array[
        'image/jpeg',
        'image/jpg',
        'image/png',
        'image/webp',
        'image/heic',
        'image/heif'
      ]::text[]
  ) then
    raise exception 'dispatch-photos bucket contract mismatch';
  end if;

  if (
    select count(*)
    from pg_publication_tables
    where pubname = 'supabase_realtime'
      and schemaname = 'public'
      and tablename = 'dispatch_notifications'
  ) <> 1 then
    raise exception 'dispatch_notifications Realtime contract mismatch';
  end if;

  raise notice 'Local-Delivery schema contract verified.';
end
$$;
