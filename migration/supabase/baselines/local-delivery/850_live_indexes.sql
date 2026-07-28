-- Index definitions present in managed Local-Delivery but absent from the
-- checked-in canonical application SQL.

create index if not exists dispatch_b2b_companies_shopify_company_id_idx
  on public.dispatch_b2b_companies (shopify_company_id);

create index if not exists dispatch_b2b_companies_shopify_location_id_idx
  on public.dispatch_b2b_companies (shopify_location_id);

create index if not exists dispatch_orders_created_at_desc_idx
  on public.dispatch_orders (created_at desc);

create index if not exists dispatch_orders_delivered_at_desc_idx
  on public.dispatch_orders (delivered_at desc)
  where delivered_at is not null;

create index if not exists dispatch_orders_delivery_status_created_idx
  on public.dispatch_orders (delivery_status, created_at desc);

create index if not exists dispatch_orders_email_subject_trgm_idx
  on public.dispatch_orders
  using gin (lower(email_subject) extensions.gin_trgm_ops);

create index if not exists dispatch_orders_notes_trgm_idx
  on public.dispatch_orders
  using gin (lower(notes) extensions.gin_trgm_ops);

create index if not exists dispatch_orders_order_number_lower_idx
  on public.dispatch_orders (lower(order_number));

create index if not exists dispatch_orders_requested_window_idx
  on public.dispatch_orders (requested_window);

create index if not exists dispatch_orders_route_active_sequence_idx
  on public.dispatch_orders (
    assigned_route_id,
    status,
    delivery_status,
    stop_sequence,
    created_at
  )
  where assigned_route_id is not null;

create index if not exists dispatch_orders_route_delivered_at_idx
  on public.dispatch_orders (assigned_route_id, delivered_at desc)
  where assigned_route_id is not null
    and delivered_at is not null;

create index if not exists dispatch_orders_source_created_idx
  on public.dispatch_orders (source, created_at desc);

create index if not exists dispatch_orders_status_delivered_at_idx
  on public.dispatch_orders (status, delivered_at desc)
  where delivered_at is not null;

create index if not exists dispatch_orders_status_delivery_created_idx
  on public.dispatch_orders (status, delivery_status, created_at desc);

create index if not exists dispatch_orders_time_preference_status_idx
  on public.dispatch_orders (time_preference, status);

create index if not exists dispatch_orders_updated_at_desc_idx
  on public.dispatch_orders (updated_at desc);

create index if not exists dispatch_stop_metrics_material_city_idx
  on public.dispatch_stop_metrics (material, city);
