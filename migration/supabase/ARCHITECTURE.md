# Supabase consolidation architecture

## Decision

The first migration stage preserves each managed Supabase project as an
independent compatibility boundary. It does not merge all applications into one
`public` schema.

Static inventory already shows overlapping tables such as `profiles`,
`user_roles`, `accounts`, `employees`, `equipment`, `audit_logs`, and
`work_logs`. Those tables can have different columns, RLS rules, triggers, and
business meanings. Combining them during infrastructure migration would mix two
high-risk changes:

1. moving production workloads to GHSSERVER; and
2. redesigning application data models.

Those changes will be separated.

## Local lab

The Mac compatibility lab uses the official Supabase Docker configuration,
pinned to a reviewed upstream commit. Only one restored production project is
tested in a lab stack at a time unless isolated ports, networks, volumes, and
project names are assigned.

The runtime lives under `migration/supabase/runtime/` and is ignored by Git
because it contains generated credentials and database volumes.

## Initial GHSSERVER layout

The production data-services VM will host separate project stacks or separate
databases with independently managed API/Auth/Storage boundaries. The exact
layout will be chosen after database sizes, Storage totals, traffic, and
cross-project dependencies are measured.

GHOS remains the unified user experience:

- one portal and navigation system;
- one eventual identity and role-mapping layer;
- shared health, audit, search, and notification views;
- deep links into modules;
- independently deployable services underneath.

## Identity transition

During compatibility migration, each application keeps its existing Supabase
Auth behavior. The restored self-hosted instance will issue new tokens, so users
must re-authenticate after cutover.

Single sign-on is a later controlled phase:

1. establish the GHOS identity authority and central roles;
2. teach each app to trust that identity provider;
3. map existing Supabase users to GHOS identities;
4. validate authorization and account-disable behavior;
5. retire project-local login screens only after acceptance testing.

## Public and private access

- Internal browser and application traffic uses the LAN.
- Remote employee access uses Tailscale.
- Database and administration ports are never exposed publicly.
- Shopify app proxies, customer-facing pages, email callbacks, and OAuth
  callbacks receive a deliberately configured public HTTPS endpoint.
- Managed Supabase remains a relay where necessary until the public endpoint is
  validated.

## Backup boundary

Every production stack requires:

- nightly logical database backups;
- scheduled full VM/application backups;
- Storage object backup with metadata;
- encrypted off-host copies;
- restore testing;
- retention independent of the live server.

Backups are not considered complete until a restore has passed validation.
