select 'auth.identities', count(*) from auth.identities
union all select 'auth.users', count(*) from auth.users
union all select 'public.Session', count(*) from public."Session"
union all select 'public.app_audit_log', count(*) from public.app_audit_log
union all select 'public.app_settings', count(*) from public.app_settings
union all select 'public.app_user_profiles', count(*) from public.app_user_profiles
union all select 'public.custom_delivery_quotes', count(*) from public.custom_delivery_quotes
union all select 'public.dispatch_audit_log', count(*) from public.dispatch_audit_log
union all select 'public.dispatch_b2b_companies', count(*) from public.dispatch_b2b_companies
union all select 'public.dispatch_driver_locations', count(*) from public.dispatch_driver_locations
union all select 'public.dispatch_employees', count(*) from public.dispatch_employees
union all select 'public.dispatch_notifications', count(*) from public.dispatch_notifications
union all select 'public.dispatch_orders', count(*) from public.dispatch_orders
union all select 'public.dispatch_push_subscriptions', count(*) from public.dispatch_push_subscriptions
union all select 'public.dispatch_routes', count(*) from public.dispatch_routes
union all select 'public.dispatch_settings', count(*) from public.dispatch_settings
union all select 'public.dispatch_shopify_updates', count(*) from public.dispatch_shopify_updates
union all select 'public.dispatch_stop_metrics', count(*) from public.dispatch_stop_metrics
union all select 'public.dispatch_trucks', count(*) from public.dispatch_trucks
union all select 'public.dispatch_user_roles', count(*) from public.dispatch_user_roles
union all select 'public.origin_addresses', count(*) from public.origin_addresses
union all select 'public.product_source_map', count(*) from public.product_source_map
union all select 'public.quote_tax_rate_cache', count(*) from public.quote_tax_rate_cache
union all select 'public.shipping_material_rules', count(*) from public.shipping_material_rules
union all select 'public.shopify_app_settings', count(*) from public.shopify_app_settings
union all select 'storage.buckets', count(*) from storage.buckets
union all select 'storage.objects', count(*) from storage.objects
order by 1;
