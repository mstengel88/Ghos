-- Supabase-managed projects install API role grants outside the application
-- migration chain. A clean self-hosted database does not inherit those grants.
--
-- All Ticket Printer public tables have RLS enabled. These table privileges
-- permit PostgREST to evaluate those policies; they do not grant row access by
-- themselves. The two dispatch bridge tables intentionally have no browser
-- policies and therefore remain service-role-only.

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
