select 'auth.identities', count(*) from auth.identities
union all select 'auth.users', count(*) from auth.users
union all select 'public.dump_site_entries', count(*) from public.dump_site_entries
union all select 'public.dump_site_rate_limits', count(*) from public.dump_site_rate_limits
union all select 'public.dump_site_sessions', count(*) from public.dump_site_sessions
union all select 'sequence.dump_site_201_d_order_number_seq', last_value from public.dump_site_201_d_order_number_seq
union all select 'sequence.dump_site_order_number_seq', last_value from public.dump_site_order_number_seq
union all select 'storage.buckets', count(*) from storage.buckets
union all select 'storage.objects', count(*) from storage.objects
order by 1;
