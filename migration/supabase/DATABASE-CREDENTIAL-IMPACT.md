# Managed database credential impact

Last reviewed: 2026-07-28

Status: source audit complete for the located applications; one live deployment
value remains to be classified before any managed database password is reset.

## Decision

Do not reset a managed Supabase database password merely to continue discovery.
The current Supabase MCP connections are sufficient for sanitized schema and
row reconciliation, but they are not a substitute for the exact logical dumps
required by Gate 2.

A password reset is permitted only after:

1. every direct PostgreSQL consumer for that project is identified;
2. its current connection can be updated during the same maintenance window;
3. a rollback value is stored in the approved password manager; and
4. application health checks are ready.

Resetting a database password does not rotate the project's publishable/anon or
service-role API keys. Applications using the Supabase HTTP API should continue
to work. Direct PostgreSQL clients, pooler clients, reporting tools, Prisma
applications, and backup jobs using the old password will fail until updated.

## Consumer classification

| Workload | Production data path | Direct managed PostgreSQL found in source? | Password-reset impact |
|---|---|---:|---|
| Dispatch V2 Sandbox | Supabase HTTP API with URL, anon key, and service-role key | No | No expected impact |
| ShipCalc | Supabase HTTP API with browser publishable key | No | No expected impact |
| Quote V2 business data | Supabase HTTP API with URL, anon key, and service-role key | No | No expected impact |
| Quote V2 Shopify sessions/settings | Prisma `DATABASE_URL` | Yes, but Compose defines a separate `contractor-postgres` service | Must classify the live value before reset |
| GHOS quote mirror | Supabase HTTP source configuration plus GHOS-local PostgreSQL | No | No expected impact |
| Dump Site | Supabase HTTP API and Edge Functions | No direct database variable found in located source | No expected impact |
| Ticket Printer | Supabase HTTP API and Edge Functions | No direct database variable found in located source | No expected impact |
| WinterWatch-Pro | Supabase HTTP API and Edge Functions | No direct database variable found in located source | No expected impact |
| Help Desk | Canonical application source not yet located | Unknown | Reset blocked |

The Quote V2 source explicitly says its Prisma database must be separate from
production business data. Its Compose file creates `contractor-postgres`, and
the Prisma schema contains only `Session` and `AppSettings`. The untracked live
`.env.contractor` is not present on this Mac, so the actual deployed
`DATABASE_URL` still needs a classification-only check. Do not print or copy
the URL.

## Safe live classification

Run this on the Docker host that runs `local-delivery-contractor`. It prints
only a classification and never prints the connection string:

```bash
docker exec local-delivery-contractor node -e '
const value = process.env.DATABASE_URL || "";
let kind = "missing";
if (value) {
  try {
    const url = new URL(value);
    const host = url.hostname.toLowerCase();
    kind =
      host === "contractor-postgres" ||
      host === "postgres" ||
      host === "localhost" ||
      host === "127.0.0.1"
        ? "separate-local-postgres"
        : host.endsWith(".supabase.co") ||
          host.endsWith(".pooler.supabase.com")
          ? "managed-supabase-direct"
          : "other-direct-postgres";
  } catch {
    kind = "configured-but-unparseable";
  }
}
console.log(kind);
'
```

Expected result:

```text
separate-local-postgres
```

Any other result keeps the managed database password-reset gate closed until
the deployment is corrected or its direct connection is included in the
maintenance plan.

## Exact export acquisition

The required database URLs belong only in the ignored file:

```text
migration/supabase/secrets/managed-db.env
```

Use the direct or session-pooler connection string shown by each project's
Supabase Dashboard **Connect** panel. The password must be percent-encoded in
the URL. Do not paste connection strings into chat, shell history, commits, or
screenshots.

If the current password is unavailable:

1. finish the live consumer classification;
2. choose one managed project, not all projects at once;
3. store a newly generated password in the password manager;
4. reset that project's database password in a maintenance window;
5. update every confirmed direct consumer;
6. run its application health checks;
7. create and checksum the exact export;
8. retain the managed project unchanged after the export.

API keys and database passwords are separate credentials. Do not rotate API
keys as part of this database-export step unless a separate credential-history
review requires it.

## Evidence retained

- Source searches covered the located Ticket Printer, WinterWatch-Pro,
  Dispatch V2, ShipCalc, Quote V2, and Dump Site repositories.
- The local Docker engine contained only the isolated self-hosted Supabase lab
  at the time of inspection; the live Quote V2 container was not running on the
  Mac.
- No secret values, database hosts, usernames, or passwords were retained in
  this document.
- No managed credential was changed.
