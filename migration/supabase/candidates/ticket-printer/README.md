# Ticket Printer live-schema candidate

This candidate captures schema drift discovered through read-only Supabase MCP
inspection of managed project `dlayrpnmfnbjlxgnkczv` on 2026-07-28.

The checked-in Ticket Printer migration history produces 12 public tables. The
managed project has two additional, currently empty tables:

- `dispatch_orders`
- `dispatch_routes`

`000_live_dispatch_bridge.sql` recreates their columns, constraints, indexes,
update triggers, and RLS state. It deliberately creates no RLS policies. That
matches the live contract: browser roles cannot access either table while a
privileged server-side integration can.

The candidate makes one behavior-preserving hardening change. Both update
functions use an empty `search_path` and schema-qualified built-ins, resolving
the mutable-function-search-path advisor warning without changing their result.

The unusual live default `dispatch_orders.unit = 'TonS'` is preserved exactly.
Changing or normalizing it requires an application-level compatibility review.

This file is a migration candidate, not authorization to write to production.
It is applied only to the disposable PostgreSQL 17 verification database until
the Ticket Printer cutover is approved.
