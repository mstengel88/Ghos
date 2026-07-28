# Application Supabase configuration audit

Last reviewed: 2026-07-28

## Local-Delivery consumers

### Dispatch V2 Sandbox

Canonical source:

```text
/Users/mattstengel/local-delivery/dispatch-v2-sandbox
```

Dispatch V2 already reads all Supabase connection values from its runtime
environment:

- `SUPABASE_URL`
- `SUPABASE_ANON_KEY`
- `SUPABASE_SERVICE_ROLE_KEY`

No managed Supabase project URL or JWT-shaped key was found in tracked
application source outside ignored environment files. This means the app can
move from managed Supabase to the future GHSSERVER endpoint through deployment
configuration rather than a code fork.

### ShipCalc

Canonical source:

```text
/Users/mattstengel/shipcalc2
```

Migration branch:

```text
codex/self-hosted-supabase-config
```

Verified commits:

- `4911cc7` — uses `VITE_SUPABASE_URL` and
  `VITE_SUPABASE_PUBLISHABLE_KEY` for the browser client and every Edge
  Function request;
- `61bc49a` — stops tracking `.env` while preserving the local file and adds a
  safe `.env.example`.

Acceptance:

- Vitest: 1 test passed;
- Vite production build: passed;
- no managed project URL or JWT-shaped value remains in the branch's tracked
  source;
- the existing local `.env` remained present and unchanged.

The branch must be reviewed and merged before declaring the production
ShipCalc deployment environment-switchable.

## WinterWatch-Pro consumer

Canonical source:

```text
/Users/mattstengel/winterwatch
```

WinterWatch's Edge Functions already use runtime Supabase variables. The
browser client and PWA cache configuration still embed the managed project URL,
and the client embeds its publishable key in TypeScript. The real `.env`
already defines the correct `VITE_SUPABASE_*` variables, but the application
does not consistently consume them.

A reviewed conversion is available on the WinterWatch branch:

```text
codex/self-hosted-supabase-config
```

Verified commit:

- `337bf8f` — reads the browser URL/key and PWA cache target from the runtime
  environment, stops tracking real environment files without deleting the
  local copies, and retains only a safe example.

Acceptance:

- Vitest: 1 test passed;
- Vite production/PWA build: passed;
- no managed project URL or JWT-shaped value remains in the changed
  application source; and
- unrelated local iOS/dispatch work remains unstaged and uncommitted.

The branch must be reviewed and merged before declaring the production
WinterWatch deployment environment-switchable.

## Credential-history caution

Removing an environment file from the current Git tree does not erase older
commits. Public/anonymous Supabase keys are designed for browser use, but any
restricted Google, Shopify, service-role, SMTP, or database credential that was
ever committed must be rotated and the repository history reviewed before
cutover. Database passwords are not being reset as part of this configuration
change.

## Direct database consumer audit

The located applications predominantly use Supabase through its HTTP API.
Dispatch V2, ShipCalc, Ticket Printer, WinterWatch-Pro, Dump Site, and GHOS do
not contain a direct managed PostgreSQL consumer in the reviewed source.

Quote V2 is the exception at the framework layer: Prisma requires
`DATABASE_URL` for Shopify `Session` and `AppSettings` records. Its Docker
Compose design provides a separate `contractor-postgres` container for those
two models, while quote and dispatch business data use the Supabase HTTP API.
The live container's untracked value must still be classified before any
GreenHills Quote Live database password reset.

Help Desk is retired and excluded from the active application cutover. Its
managed project remains untouched until a separate retention/archive decision
is approved.

See `DATABASE-CREDENTIAL-IMPACT.md` for the safe live check, impact matrix, and
password-reset gate. The check reports only a connection class and must never
print the URL.

## GHSSERVER cutover contract

The eventual deployment changes values, not application code:

| Runtime | Supabase base URL | Browser key |
|---|---|---|
| Managed production | Managed project URL | Managed publishable/anonymous key |
| Mac compatibility lab | `http://127.0.0.1:8000` | Lab anonymous key |
| GHSSERVER LAN | Internal HTTPS URL | Self-hosted anonymous key |
| GHSSERVER through Tailscale | Same HTTPS service name or approved private URL | Self-hosted anonymous key |

Server-only service-role keys must remain server-side and must never be placed
in a `VITE_`, `PUBLIC_`, or other browser-exposed variable.
