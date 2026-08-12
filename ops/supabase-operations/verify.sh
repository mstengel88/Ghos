#!/usr/bin/env bash
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$script_dir"

docker compose --env-file .env -f compose.yml ps

for service in db auth rest functions api-gw; do
  container="$(docker compose --env-file .env -f compose.yml ps -q "$service")"
  [[ -n "$container" ]] || { printf 'Missing container for %s\n' "$service" >&2; exit 1; }
  status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container")"
  [[ "$status" == healthy || "$status" == running ]] || {
    printf '%s is not healthy: %s\n' "$service" "$status" >&2
    exit 1
  }
done

api_port="$(sed -n 's/^OPERATIONS_API_PORT=//p' .env | tail -1)"
api_port="${api_port:-18000}"
anon_key="$(sed -n 's/^ANON_KEY=//p' .env | tail -1)"
service_key="$(sed -n 's/^SERVICE_ROLE_KEY=//p' .env | tail -1)"

curl -fsS -H "apikey: $anon_key" \
  "http://127.0.0.1:$api_port/auth/v1/health" >/dev/null
curl -fsS -H "apikey: $service_key" "http://127.0.0.1:$api_port/rest/v1/" >/dev/null

unauthorized="$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$api_port/rest/v1/")"
[[ "$unauthorized" == 401 ]] || {
  printf 'REST gateway accepted a request without an API key (HTTP %s).\n' "$unauthorized" >&2
  exit 1
}

# The public Dump endpoint must reach its own validation code, while a Ticket
# function without a user JWT must be rejected by the runtime router.
dump_code="$(curl -sS -o /dev/null -w '%{http_code}' -X POST \
  -H "apikey: $anon_key" -H 'Content-Type: application/json' -d '{}' \
  "http://127.0.0.1:$api_port/functions/v1/dump-site-api")"
[[ "$dump_code" != 401 ]] || {
  printf 'Dump Site public authorization path was blocked by global JWT verification.\n' >&2
  exit 1
}

ticket_code="$(curl -sS -o /dev/null -w '%{http_code}' -X POST \
  -H "apikey: $anon_key" -H 'Content-Type: application/json' -d '{}' \
  "http://127.0.0.1:$api_port/functions/v1/send-ticket-email")"
[[ "$ticket_code" == 401 ]] || {
  printf 'Ticket Printer function did not reject a missing user JWT (HTTP %s).\n' "$ticket_code" >&2
  exit 1
}

printf 'GHOS Operations backend health and authorization boundaries verified.\n'
