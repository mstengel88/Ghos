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
- [x] Located application source audited for direct PostgreSQL consumers
- [ ] Quote V2 live Prisma `DATABASE_URL` classified without revealing it
- [x] Help Desk confirmed retired and excluded from active application cutover

## Gate 2 — Backup and recovery

- [x] GHOS logical backup, encrypted dual-repository snapshot, retention,
      integrity-check, and automated PostgreSQL restore-drill tooling committed
- [ ] Encrypted managed database export created
- [ ] Managed database password reset impact gate passes for each project
- [ ] Auth schema recovery approach tested
- [x] Local-Delivery Storage objects and metadata exported and SHA-256 verified
- [x] Local-Delivery Storage restored to the isolated lab with byte-for-byte verification
- [ ] Edge Function source and secret-name manifest captured
- [ ] Full database, Auth, and Storage restore completed in an isolated environment
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
- [x] Captured Edge Function source hashes and secret-free HTTP contracts pass
- [x] Carrier callback rate calculation passes a mocked Google route test
- [x] Carrier vendor, distance-limit, and multi-load branches pass mocked tests
- [x] Shopify API success and error branches pass mocked tests
- [x] Local-Delivery app candidates support environment-specific Supabase URLs
      and keys (Dispatch V2 directly; ShipCalc migration branch)
- [x] WinterWatch-Pro client candidate supports environment-specific Supabase
      URL/key and PWA caching through its migration branch
- [x] GreenHills Quote Live already supports environment-specific Supabase
      configuration; its migration branch protects runtime secrets and passes
      secret-free configuration acceptance
- [x] Sanitized Local-Delivery/Quote Live key and row reconciliation completed
      without exporting customer data or secrets
- [x] Quote creator UUID rewrite and unmapped-owner quarantine behavior pass
      in the isolated reconciliation lab
- [x] Local-Delivery Storage/reference manifest records bucket objects,
      embedded database images, and unresolved photo payloads
- [x] Dump Site eight-migration schema, RLS, generated order number, rate
      limit, and CounterPoint queue workflow pass in disposable PostgreSQL 17
- [x] Dump Site live schema fingerprints, aggregate row counts, Auth, Storage,
      Realtime, extensions, and sequence state reconciled read-only
- [x] Dump Site Edge Function method, QR-token, submission-validation, and
      bridge-secret contracts pass in the isolated local Edge Runtime
- [x] Dump Site iOS/Android endpoint parity has a cutover-safe verification
      command; both clients currently remain on managed Supabase
- [x] Ticket Printer application migrations pass on disposable PostgreSQL 17
- [x] Ticket Printer Edge Functions pass secret-free local acceptance
- [x] Ticket Printer managed `pg_cron` task has a GHOS systemd replacement
- [x] Ticket Printer browser and Loadrite migration candidates require
      environment-specific Supabase configuration and pass build/tests
- [x] WinterWatch-Pro application migrations pass on disposable PostgreSQL 17
- [x] WinterWatch-Pro managed overtime scheduler has a GHOS systemd replacement
- [x] WinterWatch-Pro Edge Functions pass secret-free local acceptance
- [ ] No production secret exists in source control or container images

## Gate 4 — GHSSERVER deployment

- [ ] Dedicated data-services VM created
- [ ] Services are not installed directly on the Windows host
- [ ] Internal LAN and Tailscale access verified
- [ ] Public HTTPS route exists only for integrations that require it
- [ ] Firewall limits database and administrative ports
- [ ] Automatic VM and container startup verified
- [ ] Nightly logical backups and regular full backups tested
- [ ] GHOS backup repositories initialized on separate local and off-site targets
- [ ] Automated GHOS database restore drill passes on GHSSERVER
- [ ] Acronis alternate-location VM/host restore is tested
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
