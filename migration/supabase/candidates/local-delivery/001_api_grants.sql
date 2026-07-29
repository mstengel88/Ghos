-- Managed Supabase installs Data API privileges outside application migrations.
-- Declare them explicitly so a clean self-hosted restore does not depend on
-- project-level defaults that are absent from a new PostgreSQL database.
--
-- All 22 application tables have RLS enabled. These object privileges allow
-- PostgREST to evaluate the existing policies; they do not bypass RLS or make
-- protected rows public.

grant usage on schema public to anon, authenticated, service_role;

grant all privileges
on all tables in schema public
to anon, authenticated, service_role;

grant all privileges
on all sequences in schema public
to anon, authenticated, service_role;

grant all privileges
on all functions in schema public
to anon, authenticated, service_role;

alter default privileges for role postgres in schema public
  grant all privileges on tables to anon, authenticated, service_role;

alter default privileges for role postgres in schema public
  grant all privileges on sequences to anon, authenticated, service_role;

alter default privileges for role postgres in schema public
  grant all privileges on functions to anon, authenticated, service_role;
