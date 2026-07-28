-- Local-Delivery live security, Realtime, and Storage contract.
--
-- Run after the foundation and canonical application SQL. This file is
-- idempotent and contains no production rows or credentials.

-- Historical source SQL added this check after the managed project diverged.
-- Dispatch V2 remains authoritative, and the managed Local-Delivery project
-- currently has no check constraint on delivery_status.
alter table public.dispatch_orders
  drop constraint if exists dispatch_orders_delivery_status_check;

alter table public."Session" enable row level security;
alter table public.app_audit_log enable row level security;
alter table public.app_settings enable row level security;
alter table public.app_user_profiles enable row level security;
alter table public.custom_delivery_quotes enable row level security;
alter table public.dispatch_audit_log enable row level security;
alter table public.dispatch_b2b_companies enable row level security;
alter table public.dispatch_driver_locations enable row level security;
alter table public.dispatch_employees enable row level security;
alter table public.dispatch_notifications enable row level security;
alter table public.dispatch_orders enable row level security;
alter table public.dispatch_push_subscriptions enable row level security;
alter table public.dispatch_routes enable row level security;
alter table public.dispatch_settings enable row level security;
alter table public.dispatch_shopify_updates enable row level security;
alter table public.dispatch_stop_metrics enable row level security;
alter table public.dispatch_trucks enable row level security;
alter table public.dispatch_user_roles enable row level security;
alter table public.origin_addresses enable row level security;
alter table public.product_source_map enable row level security;
alter table public.shipping_material_rules enable row level security;
alter table public.shopify_app_settings enable row level security;

drop policy if exists "Anyone can read settings" on public.app_settings;
create policy "Anyone can read settings"
  on public.app_settings
  for select
  to anon, authenticated
  using (true);

drop policy if exists "Anyone can read active origin" on public.origin_addresses;
create policy "Anyone can read active origin"
  on public.origin_addresses
  for select
  using (true);

drop policy if exists "dispatch authenticated read audit"
  on public.dispatch_audit_log;
create policy "dispatch authenticated read audit"
  on public.dispatch_audit_log
  for select
  to authenticated
  using (true);

drop policy if exists "dispatch active users notifications"
  on public.dispatch_notifications;
create policy "dispatch active users notifications"
  on public.dispatch_notifications
  for all
  to authenticated
  using (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  )
  with check (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  );

drop policy if exists "dispatch active users push subscriptions"
  on public.dispatch_push_subscriptions;
create policy "dispatch active users push subscriptions"
  on public.dispatch_push_subscriptions
  for all
  to authenticated
  using (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  )
  with check (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  );

drop policy if exists "dispatch authenticated read shopify updates"
  on public.dispatch_shopify_updates;
create policy "dispatch authenticated read shopify updates"
  on public.dispatch_shopify_updates
  for select
  to authenticated
  using (true);

drop policy if exists "dispatch active users stop metrics"
  on public.dispatch_stop_metrics;
create policy "dispatch active users stop metrics"
  on public.dispatch_stop_metrics
  for all
  to authenticated
  using (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  )
  with check (
    exists (
      select 1
      from public.dispatch_user_roles r
      where r.user_id = auth.uid()
        and r.is_active = true
    )
  );

drop policy if exists "dispatch authenticated user roles read"
  on public.dispatch_user_roles;
create policy "dispatch authenticated user roles read"
  on public.dispatch_user_roles
  for select
  to authenticated
  using (true);

do $$
begin
  if not exists (
    select 1
    from pg_publication_tables
    where pubname = 'supabase_realtime'
      and schemaname = 'public'
      and tablename = 'dispatch_notifications'
  ) then
    alter publication supabase_realtime
      add table public.dispatch_notifications;
  end if;
end
$$;

insert into storage.buckets (
  id,
  name,
  public,
  file_size_limit,
  allowed_mime_types
)
values (
  'dispatch-photos',
  'dispatch-photos',
  true,
  10485760,
  array[
    'image/jpeg',
    'image/jpg',
    'image/png',
    'image/webp',
    'image/heic',
    'image/heif'
  ]
)
on conflict (id) do update
set public = excluded.public,
    file_size_limit = excluded.file_size_limit,
    allowed_mime_types = excluded.allowed_mime_types;

notify pgrst, 'reload schema';
