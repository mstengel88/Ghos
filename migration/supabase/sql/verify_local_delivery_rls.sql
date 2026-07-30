\set ON_ERROR_STOP on
\pset pager off

begin;

create or replace function pg_temp.assert_count(
  actual_count bigint,
  expected_count bigint,
  check_name text
)
returns void
language plpgsql
as $$
begin
  if actual_count <> expected_count then
    raise exception
      'RLS check failed for %: expected %, found %',
      check_name,
      expected_count,
      actual_count;
  end if;
end
$$;

insert into auth.users (
  id,
  aud,
  role,
  email,
  created_at,
  updated_at
)
values
  (
    '10000000-0000-0000-0000-000000000001',
    'authenticated',
    'authenticated',
    'active@example.invalid',
    now(),
    now()
  ),
  (
    '10000000-0000-0000-0000-000000000002',
    'authenticated',
    'authenticated',
    'admin@example.invalid',
    now(),
    now()
  ),
  (
    '10000000-0000-0000-0000-000000000003',
    'authenticated',
    'authenticated',
    'inactive@example.invalid',
    now(),
    now()
  );

insert into public.app_user_profiles (
  id,
  email,
  name,
  role,
  permissions,
  is_active
)
values
  (
    '10000000-0000-0000-0000-000000000001',
    'active@example.invalid',
    'Active Test User',
    'user',
    '["quoteTool"]',
    true
  ),
  (
    '10000000-0000-0000-0000-000000000002',
    'admin@example.invalid',
    'Admin Test User',
    'admin',
    '["settings"]',
    true
  ),
  (
    '10000000-0000-0000-0000-000000000003',
    'inactive@example.invalid',
    'Inactive Test User',
    'user',
    '["quoteTool"]',
    false
  );

insert into public.dispatch_user_roles (
  user_id,
  email,
  display_name,
  role,
  permissions,
  is_active
)
values
  (
    '10000000-0000-0000-0000-000000000001',
    'active@example.invalid',
    'Active Test User',
    'viewer',
    array[]::text[],
    true
  ),
  (
    '10000000-0000-0000-0000-000000000002',
    'admin@example.invalid',
    'Admin Test User',
    'admin',
    array['settings']::text[],
    true
  ),
  (
    '10000000-0000-0000-0000-000000000003',
    'inactive@example.invalid',
    'Inactive Test User',
    'viewer',
    array[]::text[],
    false
  );

insert into public.app_settings (
  key,
  value,
  label
)
values (
  'rls-rehearsal',
  'enabled',
  'RLS rehearsal'
);

insert into public.origin_addresses (
  label,
  address,
  is_active
)
values (
  'RLS rehearsal',
  'Local lab only',
  true
);

insert into public.dispatch_audit_log (
  action,
  actor,
  message
)
values (
  'rls_rehearsal',
  'local-lab',
  'Transaction-only RLS fixture'
);

insert into public.dispatch_notifications (
  target_role,
  title,
  message
)
values (
  'loader',
  'RLS rehearsal',
  'Transaction-only RLS fixture'
);

insert into public.dispatch_b2b_companies (
  id,
  shopify_company_id,
  company_name
)
values (
  'rls-rehearsal',
  'gid://shopify/Company/0',
  'RLS Rehearsal Company'
);

insert into public.quote_tax_rate_cache (
  cache_key,
  rate
)
values (
  'rls-rehearsal',
  0.055000
);

set local role anon;
select set_config(
  'request.jwt.claims',
  '{"role":"anon"}',
  true
);
select pg_temp.assert_count(
  (select count(*) from public.app_settings),
  1,
  'anonymous settings read'
);
select pg_temp.assert_count(
  (select count(*) from public.origin_addresses),
  1,
  'anonymous origin read'
);
select pg_temp.assert_count(
  (select count(*) from public.quote_tax_rate_cache),
  0,
  'anonymous tax cache isolation'
);

reset role;
set local role authenticated;
select set_config(
  'request.jwt.claims',
  '{"sub":"10000000-0000-0000-0000-000000000001","role":"authenticated"}',
  true
);
select pg_temp.assert_count(
  (select count(*) from public.app_user_profiles),
  1,
  'active user own profile'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_audit_log),
  1,
  'authenticated audit read'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_user_roles),
  3,
  'authenticated role directory read'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_b2b_companies),
  1,
  'active dispatch user B2B read'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_notifications),
  1,
  'active dispatch user notification read'
);
select pg_temp.assert_count(
  (select count(*) from public.quote_tax_rate_cache),
  0,
  'authenticated tax cache isolation'
);

reset role;
set local role authenticated;
select set_config(
  'request.jwt.claims',
  '{"sub":"10000000-0000-0000-0000-000000000003","role":"authenticated"}',
  true
);
select pg_temp.assert_count(
  (select count(*) from public.app_user_profiles),
  1,
  'inactive user own profile'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_b2b_companies),
  0,
  'inactive dispatch user B2B isolation'
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_notifications),
  0,
  'inactive dispatch user notification isolation'
);

reset role;
set local role authenticated;
select set_config(
  'request.jwt.claims',
  '{"sub":"10000000-0000-0000-0000-000000000002","role":"authenticated"}',
  true
);
select pg_temp.assert_count(
  (select count(*) from public.dispatch_b2b_companies),
  1,
  'active admin B2B read'
);

reset role;
set local role service_role;
select set_config(
  'request.jwt.claims',
  '{"role":"service_role"}',
  true
);
select pg_temp.assert_count(
  (select count(*) from public.app_user_profiles),
  3,
  'service role profile management visibility'
);
select pg_temp.assert_count(
  (select count(*) from public.quote_tax_rate_cache),
  1,
  'service role tax cache visibility'
);

reset role;
rollback;

do $$
begin
  if exists (
    select 1
    from auth.users
    where email like '%@example.invalid'
  ) then
    raise exception 'RLS rehearsal left Auth fixtures behind';
  end if;

  if exists (
    select 1
    from public.app_settings
    where key = 'rls-rehearsal'
  ) then
    raise exception 'RLS rehearsal left application fixtures behind';
  end if;

  raise notice 'Local-Delivery RLS behavior verified.';
end
$$;
