# Local-Delivery Edge Function migration candidate

Status: local compatibility acceptance

This candidate starts from the byte-for-byte deployed source preserved under
`baselines/local-delivery/functions`. It does not replace that evidence.

## Reviewed carrier-service correction

The deployed carrier helper referenced `RATE_PER_MINUTE` outside the request
scope where it was declared. The candidate:

- passes the database-derived rate into the route helper explicitly;
- moves deterministic route-price math into `delivery-math.ts`;
- rounds the delivery price to cents;
- clamps negative duration, distance, and rate inputs;
- permits a local-only `GOOGLE_DISTANCE_MATRIX_URL` override for tests while
  retaining the official Google URL as the runtime default.

Candidate hashes:

| Source | SHA-256 |
|---|---|
| `functions/carrier-service/index.ts` | `0a9c55eccebf35aca43ac34c866d95380cc00cba15b21100c07550a93c2cff27` |
| `functions/carrier-service/delivery-math.ts` | `b44a8388763900d61fb08ce5c760885530d989f5b5120629cf2f56e26872711f` |
| `functions/shopify-api/index.ts` | `e8fde587e01520d9c87a6dafec30baa5f3b3730d1b43a5d196fcb68c7d940aee` |
| `functions/shopify-api/shipping-calc.ts` | `2d9384f4d5219b32515aa274986b8db04dc8665eaf7626eedb262f21ed68e407` |

## Acceptance

Mount the candidate into the isolated lab:

```bash
cd migration/supabase/runtime/stack

docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  -f ../../docker-compose.mailpit.yml \
  -f ../../docker-compose.edge-functions-candidate.yml \
  up -d --no-deps functions
```

Run:

```bash
tools/verify_local_delivery_edge_candidate.sh
tools/verify_local_delivery_edge_functions.sh
tools/verify_local_delivery_clean_room.sh
```

The candidate test starts a temporary localhost-only route mock. It verifies
that a 15-minute, 10-mile one-way route produces a 30-minute round trip and a
`$62.40` carrier rate at `$2.08` per minute. The mock is stopped automatically.

This candidate is not approved for external callback cutover yet. Shopify API
success/error behavior, vendor-origin selection, mileage rejection, multi-load
pricing, and callback authentication still need mocked acceptance.
