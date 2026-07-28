-- Local-Delivery compatibility foundation
--
-- This file reconstructs the seven public tables that exist in the managed
-- project but are not created from zero by the canonical local SQL sources.
-- It contains schema only: no production rows, identities, sessions, secrets,
-- or Shopify tokens.

create extension if not exists pgcrypto with schema extensions;
create extension if not exists pg_trgm with schema extensions;
create extension if not exists "uuid-ossp" with schema extensions;

create table public."Session" (
  id text primary key,
  shop text not null,
  state text not null,
  "isOnline" boolean not null default false,
  scope text,
  expires timestamptz,
  "accessToken" text not null,
  "userId" bigint,
  "firstName" text,
  "lastName" text,
  email text,
  "accountOwner" boolean,
  locale text,
  collaborator boolean,
  "emailVerified" boolean,
  "refreshToken" text,
  "refreshTokenExpires" timestamptz
);

create table public.app_settings (
  key text primary key,
  value text not null,
  label text not null default '',
  description text not null default '',
  updated_at timestamptz not null default now(),
  use_test_flat_rate boolean default false,
  test_flat_rate_cents integer default 5000,
  enable_calculated_rates boolean default true,
  enable_remote_surcharge boolean default true,
  enable_debug_logging boolean default false,
  show_vendor_source boolean default true,
  shop text
);

create unique index app_settings_shop_idx
  on public.app_settings (shop);

-- Both names exist in the managed project. Preserve them in the compatibility
-- baseline even though they currently enforce the same nullable uniqueness.
create unique index app_settings_shop_unique
  on public.app_settings (shop);

create table public.custom_delivery_quotes (
  id uuid primary key default gen_random_uuid(),
  shop text not null,
  customer_name text,
  address1 text not null,
  address2 text,
  city text not null,
  province text not null,
  postal_code text not null,
  country text not null default 'US',
  quote_total_cents integer not null default 0,
  service_name text,
  description text,
  eta text,
  summary text,
  source_breakdown jsonb not null default '[]'::jsonb,
  line_items jsonb not null default '[]'::jsonb,
  created_at timestamptz not null default now(),
  customer_email text,
  customer_phone text,
  shipping_details text,
  created_by_user_id uuid references auth.users(id) on delete set null,
  created_by_name text,
  created_by_email text,
  updated_at timestamptz,
  company_name text,
  shopify_company_id text,
  shopify_company_location_id text,
  payment_terms_name text,
  payment_terms_template_id text,
  payment_terms_due_in_days integer,
  tax_exempt boolean not null default false,
  billing_address1 text,
  billing_address2 text,
  billing_city text,
  billing_province text,
  billing_postal_code text,
  billing_country text default 'US',
  shopify_company_contact_id text
);

create index custom_delivery_quotes_company_name_idx
  on public.custom_delivery_quotes (company_name);
create index custom_delivery_quotes_created_at_idx
  on public.custom_delivery_quotes (created_at desc);
create index custom_delivery_quotes_created_by_user_id_idx
  on public.custom_delivery_quotes (created_by_user_id);
create index custom_delivery_quotes_payment_terms_template_idx
  on public.custom_delivery_quotes (payment_terms_template_id);
create index custom_delivery_quotes_shopify_company_id_idx
  on public.custom_delivery_quotes (shopify_company_id);

create table public.origin_addresses (
  id uuid primary key default gen_random_uuid(),
  label text not null default 'Default',
  address text not null,
  is_active boolean not null default false,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table public.product_source_map (
  id uuid primary key default gen_random_uuid(),
  sku text not null unique,
  product_title text not null,
  pickup_vendor text,
  created_at timestamptz not null default now(),
  image_url text,
  updated_at timestamptz not null default now(),
  price numeric,
  variant_id text,
  contractor_tier_1_price numeric,
  contractor_tier_2_price numeric,
  unit_label text
);

create table public.shipping_material_rules (
  prefix text primary key,
  material_name text not null,
  truck_capacity integer not null,
  is_active boolean not null default true,
  sort_order integer not null default 0,
  updated_at timestamptz not null default now(),
  vendor_source text
);

create index shipping_material_rules_active_idx
  on public.shipping_material_rules (is_active, sort_order);

create table public.shopify_app_settings (
  shop text primary key,
  use_test_flat_rate boolean default false,
  test_flat_rate_cents integer default 5000,
  enable_calculated_rates boolean default true,
  enable_remote_surcharge boolean default true,
  enable_debug_logging boolean default false,
  show_vendor_source boolean default true,
  updated_at timestamptz default now()
);
