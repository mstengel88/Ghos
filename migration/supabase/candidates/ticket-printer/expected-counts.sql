select 'auth.identities', count(*) from auth.identities
union all select 'auth.users', count(*) from auth.users
union all select 'public.agent_registry', count(*) from public.agent_registry
union all select 'public.audit_logs', count(*) from public.audit_logs
union all select 'public.customers', count(*) from public.customers
union all select 'public.dispatch_orders', count(*) from public.dispatch_orders
union all select 'public.dispatch_routes', count(*) from public.dispatch_routes
union all select 'public.feedback', count(*) from public.feedback
union all select 'public.orders', count(*) from public.orders
union all select 'public.products', count(*) from public.products
union all select 'public.profiles', count(*) from public.profiles
union all select 'public.template_versions', count(*) from public.template_versions
union all select 'public.ticket_templates', count(*) from public.ticket_templates
union all select 'public.tickets', count(*) from public.tickets
union all select 'public.trucks', count(*) from public.trucks
union all select 'public.user_roles', count(*) from public.user_roles
union all select 'storage.buckets', count(*) from storage.buckets
union all select 'storage.objects', count(*) from storage.objects
order by 1;
