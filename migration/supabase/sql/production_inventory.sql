\pset pager off
\timing off

-- Run read-only against one managed project at a time.
-- This script reports metadata only. It does not return application rows,
-- Auth identities, secrets, Vault contents, or Storage object names.

select
  current_database() as database_name,
  current_setting('server_version') as postgres_version,
  pg_size_pretty(pg_database_size(current_database())) as database_size;

select extname, extversion
from pg_extension
order by extname;

select schema_name
from information_schema.schemata
where schema_name not like 'pg_%'
  and schema_name <> 'information_schema'
order by schema_name;

select
  schemaname,
  relname as relation_name,
  case relkind
    when 'r' then 'table'
    when 'p' then 'partitioned table'
    when 'v' then 'view'
    when 'm' then 'materialized view'
    when 'S' then 'sequence'
    else relkind::text
  end as relation_type,
  pg_size_pretty(pg_total_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname))) as total_size
from pg_stat_user_tables
join pg_class on pg_class.oid = (quote_ident(schemaname) || '.' || quote_ident(relname))::regclass
where schemaname not in ('auth', 'storage', 'extensions', 'realtime')
order by pg_total_relation_size(quote_ident(schemaname) || '.' || quote_ident(relname)) desc;

select
  n.nspname as schema_name,
  p.proname as function_name,
  pg_get_function_identity_arguments(p.oid) as arguments
from pg_proc p
join pg_namespace n on n.oid = p.pronamespace
where n.nspname not in ('pg_catalog', 'information_schema')
order by n.nspname, p.proname, arguments;

select
  event_object_schema as table_schema,
  event_object_table as table_name,
  trigger_name,
  action_timing,
  event_manipulation
from information_schema.triggers
order by event_object_schema, event_object_table, trigger_name;

-- Some managed read-only roles do not expose application triggers through
-- information_schema.triggers. Query pg_trigger as a second authoritative
-- inventory path.
select
  n.nspname as table_schema,
  c.relname as table_name,
  t.tgname as trigger_name,
  pg_get_triggerdef(t.oid, true) as trigger_definition
from pg_trigger t
join pg_class c on c.oid = t.tgrelid
join pg_namespace n on n.oid = c.relnamespace
where not t.tgisinternal
order by n.nspname, c.relname, t.tgname;

select schemaname, tablename, policyname, permissive, roles, cmd
from pg_policies
order by schemaname, tablename, policyname;

select schemaname, tablename
from pg_publication_tables
order by schemaname, tablename;

select
  count(*) as auth_user_count,
  count(*) filter (where email_confirmed_at is not null) as confirmed_email_count,
  count(*) filter (where phone_confirmed_at is not null) as confirmed_phone_count
from auth.users;

select
  id as bucket_id,
  public,
  file_size_limit,
  allowed_mime_types,
  count(objects.id) as object_count,
  coalesce(sum((objects.metadata ->> 'size')::bigint), 0) as total_bytes
from storage.buckets
left join storage.objects on storage.objects.bucket_id = storage.buckets.id
group by storage.buckets.id, storage.buckets.public,
         storage.buckets.file_size_limit, storage.buckets.allowed_mime_types
order by storage.buckets.id;

select
  exists (
    select 1
    from information_schema.tables
    where table_schema = 'cron' and table_name = 'job'
  ) as pg_cron_schema_present;
