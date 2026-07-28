# Managed Supabase to GHSSERVER migration runbook

## Gate 0 — Hardware

- [ ] Dell diagnostics complete without memory errors
- [ ] Replacement RAM installed
- [x] Extended memory test passes (reported 2026-07-27)
- [ ] RAID and storage health verified
- [ ] UPS shutdown behavior verified

No production data service is deployed before Gate 0 passes.

## Gate 1 — Application inventory

For each of the five production applications:

- [ ] Canonical repository identified
- [ ] Managed Supabase project identified
- [ ] Owners and business criticality documented
- [ ] Database schemas, extensions, tables, views, functions, triggers, RLS,
      policies, webhooks, cron jobs, and Realtime publications inventoried
- [ ] Auth providers, roles, users, email delivery, MFA, and redirect URLs inventoried
- [ ] Storage buckets, object totals, policies, and URL dependencies inventoried
- [ ] Edge Functions, environment secrets, schedules, and external APIs inventoried
- [ ] All clients and service-role consumers identified

## Gate 2 — Backup and recovery

- [ ] Encrypted managed database export created
- [ ] Auth schema recovery approach tested
- [ ] Storage objects and metadata exported
- [ ] Edge Function source and secret-name manifest captured
- [ ] Restore completed into an isolated local environment
- [ ] Restore validation report retained
- [ ] Off-host backup copy created

## Gate 3 — Local compatibility

- [x] Isolated PostgreSQL 17 Supabase lab starts with all services healthy
- [x] Self-hosted smoke test passes before and after Local-Delivery restore
  (35/35 most recently on 2026-07-28)
- [x] Local-Delivery database migrations apply cleanly from an empty lab
- [x] Local-Delivery RLS role behavior passes transaction-only acceptance tests
- [x] Auth create, password login/change, profile, logout, refresh revocation,
  deletion, and role behavior work
- [x] Auth password recovery and invitations pass through a local email catcher
- [x] Storage upload, download, integrity, signed URL, public URL, and TUS
  resumable behavior works
- [ ] Edge Functions pass functional tests
- [ ] App configuration supports environment-specific Supabase URLs and keys
- [ ] No production secret exists in source control or container images

## Gate 4 — GHSSERVER deployment

- [ ] Dedicated data-services VM created
- [ ] Services are not installed directly on the Windows host
- [ ] Internal LAN and Tailscale access verified
- [ ] Public HTTPS route exists only for integrations that require it
- [ ] Firewall limits database and administrative ports
- [ ] Automatic VM and container startup verified
- [ ] Nightly logical backups and regular full backups tested
- [ ] Monitoring and capacity alerts enabled

## Gate 5 — Per-application cutover

- [ ] Final delta or maintenance-window process rehearsed
- [ ] Record counts and critical-table checksums match
- [ ] Auth and authorization acceptance tests pass
- [ ] Storage object counts and sample hashes match
- [ ] External integrations pass
- [ ] Application switches through configuration, not a code fork
- [ ] Rollback procedure and decision deadline documented
- [ ] Managed Supabase remains intact during the observation window

## Gate 6 — GHOS unification

- [ ] App appears as a GHOS module
- [ ] Central role mapping defined
- [ ] Single sign-on works
- [ ] Audit events reach the GHOS activity timeline
- [ ] Health and dependency status appear in GHOS
- [ ] Deep links and mobile/PWA behavior work

## Gate 7 — Managed Supabase reduction

- [ ] Every production consumer has been observed on GHSSERVER
- [ ] No managed-project writes occur during the final verification period
- [ ] Billing and retention implications reviewed
- [ ] Final managed export archived
- [ ] Project is scaled down before any irreversible deletion
