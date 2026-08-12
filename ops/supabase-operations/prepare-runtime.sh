#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
upstream="$repo_root/migration/supabase/runtime/stack/volumes"
runtime="$script_dir/.runtime"
ticket_source="${TICKET_PRINTER_SOURCE:-/opt/ghos/apps/ticket-creator/supabase/functions}"
dump_source="${DUMP_SITE_SOURCE:-/opt/ghos/apps/dump-site-source/supabase/functions}"

for path in \
  "$upstream/api/envoy" \
  "$upstream/db/roles.sql" \
  "$upstream/db/jwt.sql" \
  "$upstream/functions/main/index.ts" \
  "$ticket_source" \
  "$dump_source"; do
  if [[ ! -e "$path" ]]; then
    printf 'Required source not found: %s\n' "$path" >&2
    exit 1
  fi
done

rm -rf "$runtime"
mkdir -p "$runtime/api" "$runtime/db" "$runtime/functions/main"
cp "$upstream/api/envoy/envoy.yaml" "$runtime/api/envoy.yaml"
cp "$upstream/api/envoy/cds.yaml" "$runtime/api/cds.yaml"
cp "$upstream/api/envoy/lds.template.yaml" "$runtime/api/lds.template.yaml"
cp "$upstream/api/envoy/docker-entrypoint.sh" "$runtime/api/docker-entrypoint.sh"
cp "$upstream/db/roles.sql" "$runtime/db/roles.sql"
cp "$upstream/db/jwt.sql" "$runtime/db/jwt.sql"
cat > "$runtime/db/bootstrap-roles.sql" <<'SQL'
\set pgpass `echo "$POSTGRES_PASSWORD"`

-- The pinned Supabase image creates a different subset of service roles
-- depending on which optional services are enabled. Assign the shared
-- database password only to roles that exist in this lightweight stack.
SELECT format('ALTER ROLE %I PASSWORD %L;', rolname, :'pgpass')
FROM pg_roles
WHERE rolname IN (
  'authenticator',
  'pgbouncer',
  'supabase_auth_admin',
  'supabase_functions_admin',
  'supabase_storage_admin'
)
\gexec

SELECT format(
  'ALTER FUNCTION %I.%I(%s) OWNER TO supabase_auth_admin;',
  namespace.nspname,
  procedure.proname,
  pg_get_function_identity_arguments(procedure.oid)
)
FROM pg_proc AS procedure
JOIN pg_namespace AS namespace ON namespace.oid = procedure.pronamespace
WHERE namespace.nspname = 'auth'
  AND procedure.proname IN ('uid', 'role', 'email')
  AND EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'supabase_auth_admin')
\gexec
SQL
cat > "$runtime/db/auth-owner.sql" <<'SQL'
DO $$
BEGIN
  IF to_regprocedure('auth.uid()') IS NOT NULL THEN
    ALTER FUNCTION auth.uid() OWNER TO supabase_auth_admin;
  END IF;
  IF to_regprocedure('auth.role()') IS NOT NULL THEN
    ALTER FUNCTION auth.role() OWNER TO supabase_auth_admin;
  END IF;
  IF to_regprocedure('auth.email()') IS NOT NULL THEN
    ALTER FUNCTION auth.email() OWNER TO supabase_auth_admin;
  END IF;
END
$$;
SQL
cp "$upstream/functions/main/index.ts" "$runtime/functions/main/index.ts"

# Preserve the official runtime verifier for Ticket Printer functions while
# allowing only the two Dump Site endpoints to use their existing in-function
# QR/bridge authorization contract.
python3 - "$runtime/functions/main/index.ts" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
source = path.read_text()

needle = "const VERIFY_JWT = Deno.env.get('VERIFY_JWT') === 'true'\n"
replacement = needle + "const PUBLIC_FUNCTIONS = new Set((Deno.env.get('PUBLIC_FUNCTIONS') ?? '').split(',').map((value) => value.trim()).filter(Boolean))\n"
if source.count(needle) != 1:
    raise SystemExit('Pinned Edge Runtime router changed: VERIFY_JWT marker mismatch')
source = source.replace(needle, replacement)

old = "Deno.serve(async (req: Request) => {\n  if (req.method !== 'OPTIONS' && VERIFY_JWT) {"
new = "Deno.serve(async (req: Request) => {\n  const url = new URL(req.url)\n  const { pathname } = url\n  const path_parts = pathname.split('/')\n  const service_name = path_parts[1]\n  const requiresJwt = VERIFY_JWT && !PUBLIC_FUNCTIONS.has(service_name)\n\n  if (req.method !== 'OPTIONS' && requiresJwt) {"
if source.count(old) != 1:
    raise SystemExit('Pinned Edge Runtime router changed: handler marker mismatch')
source = source.replace(old, new)

duplicate = "\n  const url = new URL(req.url)\n  const { pathname } = url\n  const path_parts = pathname.split('/')\n  const service_name = path_parts[1]\n"
if source.count(duplicate) != 2:
    raise SystemExit('Pinned Edge Runtime router changed: route parser count mismatch')
first = source.find(duplicate)
second = source.find(duplicate, first + len(duplicate))
if second == -1:
    raise SystemExit('Pinned Edge Runtime router changed: second route parser not found')
source = source[:second] + source[second + len(duplicate):]
path.write_text(source)
PY

for source_root in "$ticket_source" "$dump_source"; do
  while IFS= read -r -d '' function_dir; do
    function_name="$(basename "$function_dir")"
    [[ "$function_name" == "_shared" ]] && continue
    if [[ -e "$runtime/functions/$function_name" ]]; then
      printf 'Duplicate Edge Function name: %s\n' "$function_name" >&2
      exit 1
    fi
    cp -R "$function_dir" "$runtime/functions/$function_name"
  done < <(find "$source_root" -mindepth 1 -maxdepth 1 -type d -print0)
done

if [[ -d "$ticket_source/_shared" ]]; then
  cp -R "$ticket_source/_shared" "$runtime/functions/_shared"
fi

chmod -R go-rwx "$runtime"
printf 'Prepared GHOS Operations runtime with %s Edge Functions.\n' \
  "$(find "$runtime/functions" -mindepth 1 -maxdepth 1 -type d ! -name main ! -name _shared | wc -l | tr -d ' ')"
