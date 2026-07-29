-- Ticket Printer managed-project drift captured read-only on 2026-07-28.
--
-- These tables are currently empty in the managed project. RLS is enabled and
-- no policies are intentionally created, matching the live API access contract:
-- only privileged server-side roles can access the bridge.

create table if not exists public.dispatch_routes (
  id text primary key,
  code text not null default '',
  truck text not null default '',
  driver text not null default '',
  helper text not null default '',
  color text not null default '#38bdf8',
  shift text not null default '',
  region text not null default '',
  is_active boolean not null default true,
  created_at timestamptz not null default timezone('utc', now()),
  updated_at timestamptz not null default timezone('utc', now())
);

create index if not exists dispatch_routes_active_idx
  on public.dispatch_routes (is_active, created_at);

create table if not exists public.dispatch_orders (
  id text primary key,
  source text not null
    constraint dispatch_orders_source_check
    check (source in ('email', 'manual')),
  customer text not null default '',
  contact text not null default '',
  address text not null default '',
  city text not null default '',
  material text not null default '',
  quantity text not null default '',
  unit text not null default 'TonS',
  requested_window text not null default 'Needs scheduling',
  truck_preference text,
  notes text not null default '',
  status text not null default 'new'
    constraint dispatch_orders_status_check
    check (status in ('new', 'scheduled', 'hold')),
  assigned_route_id text,
  created_at timestamptz not null default timezone('utc', now()),
  updated_at timestamptz not null default timezone('utc', now())
);

create index if not exists dispatch_orders_assigned_route_idx
  on public.dispatch_orders (assigned_route_id);

create index if not exists dispatch_orders_status_idx
  on public.dispatch_orders (status, created_at desc);

create or replace function public.set_dispatch_routes_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = pg_catalog.timezone('utc', pg_catalog.now());
  return new;
end;
$$;

create or replace function public.set_dispatch_orders_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = pg_catalog.timezone('utc', pg_catalog.now());
  return new;
end;
$$;

drop trigger if exists dispatch_routes_set_updated_at
  on public.dispatch_routes;
create trigger dispatch_routes_set_updated_at
before update on public.dispatch_routes
for each row
execute function public.set_dispatch_routes_updated_at();

drop trigger if exists dispatch_orders_set_updated_at
  on public.dispatch_orders;
create trigger dispatch_orders_set_updated_at
before update on public.dispatch_orders
for each row
execute function public.set_dispatch_orders_updated_at();

alter table public.dispatch_routes enable row level security;
alter table public.dispatch_orders enable row level security;
