#!/usr/bin/env bash
set -euo pipefail

db_container="${SUPABASE_DB_CONTAINER:-supabase-db}"

if [[ "${ALLOW_LOCAL_REHEARSAL_RESET:-}" != "yes" ]]; then
  echo "Refusing reset. Set ALLOW_LOCAL_REHEARSAL_RESET=yes for the isolated lab." >&2
  exit 1
fi

if [[ "$db_container" != "supabase-db" ]]; then
  echo "Refusing reset for unexpected container: $db_container" >&2
  exit 1
fi

docker exec -i "$db_container" \
  psql -v ON_ERROR_STOP=1 -U postgres -d postgres <<'SQL'
do $$
declare
  application_table text;
  application_row_count bigint;
  application_tables constant text[] := array[
    'Session',
    'app_audit_log',
    'app_settings',
    'app_user_profiles',
    'custom_delivery_quotes',
    'dispatch_audit_log',
    'dispatch_b2b_companies',
    'dispatch_driver_locations',
    'dispatch_employees',
    'dispatch_notifications',
    'dispatch_orders',
    'dispatch_push_subscriptions',
    'dispatch_routes',
    'dispatch_settings',
    'dispatch_shopify_updates',
    'dispatch_stop_metrics',
    'dispatch_trucks',
    'dispatch_user_roles',
    'origin_addresses',
    'product_source_map',
    'quote_tax_rate_cache',
    'shipping_material_rules',
    'shopify_app_settings'
  ];
begin
  foreach application_table in array application_tables loop
    if to_regclass(format('public.%I', application_table)) is not null then
      execute format(
        'select count(*) from public.%I',
        application_table
      ) into application_row_count;

      if application_row_count <> 0 then
        raise exception
          'Refusing reset: public.% contains % row(s)',
          application_table,
          application_row_count;
      end if;
    end if;
  end loop;

  if exists (select 1 from auth.users) then
    raise exception 'Refusing reset: local Auth users exist';
  end if;

  if exists (
    select 1
    from storage.objects
    where bucket_id = 'dispatch-photos'
  ) then
    raise exception 'Refusing reset: dispatch-photos contains objects';
  end if;
end
$$;

do $$
begin
  if exists (
    select 1
    from pg_publication_tables
    where pubname = 'supabase_realtime'
      and schemaname = 'public'
      and tablename = 'dispatch_notifications'
  ) then
    alter publication supabase_realtime
      drop table public.dispatch_notifications;
  end if;
end
$$;

drop table if exists
  public."Session",
  public.app_audit_log,
  public.app_settings,
  public.app_user_profiles,
  public.custom_delivery_quotes,
  public.dispatch_audit_log,
  public.dispatch_b2b_companies,
  public.dispatch_driver_locations,
  public.dispatch_employees,
  public.dispatch_notifications,
  public.dispatch_orders,
  public.dispatch_push_subscriptions,
  public.dispatch_routes,
  public.dispatch_settings,
  public.dispatch_shopify_updates,
  public.dispatch_stop_metrics,
  public.dispatch_trucks,
  public.dispatch_user_roles,
  public.origin_addresses,
  public.product_source_map,
  public.quote_tax_rate_cache,
  public.shipping_material_rules,
  public.shopify_app_settings
cascade;

drop function if exists public.set_dispatch_b2b_companies_updated_at();
drop function if exists public.set_dispatch_driver_locations_updated_at();
drop function if exists public.set_dispatch_employees_updated_at();
drop function if exists public.set_dispatch_orders_updated_at();
drop function if exists public.set_dispatch_routes_updated_at();
drop function if exists public.set_dispatch_stop_metrics_updated_at();
drop function if exists public.set_dispatch_trucks_updated_at();
drop function if exists public.set_dispatch_user_roles_updated_at();
SQL

echo "Local-Delivery rehearsal objects reset."
