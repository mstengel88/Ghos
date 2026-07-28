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
- permits an explicit `SHOPIFY_API_BASE_URL` override for isolated acceptance
  tests while retaining the official Shopify store URL as the runtime default.

Candidate hashes:

| Source | SHA-256 |
|---|---|
| `functions/carrier-service/index.ts` | `84f7ffcff70d1e2ec9194dd76c8e82aae416ecbf6447d82262c46352ac17a751` |
| `functions/carrier-service/delivery-math.ts` | `b44a8388763900d61fb08ce5c760885530d989f5b5120629cf2f56e26872711f` |
| `functions/shopify-api/index.ts` | `91f1799b33ba7f55630781e04ed7bdfaf762671b08072a5a8d0360c7bc4f301f` |
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
tools/verify_local_delivery_clean_room.sh
```

Then return the functions container to the captured secret-free baseline:

```bash
cd migration/supabase/runtime/stack

docker compose \
  --env-file .env \
  -f docker-compose.yml \
  -f docker-compose.pg17.yml \
  -f ../../docker-compose.macos-storage.yml \
  -f ../../docker-compose.mailpit.yml \
  -f ../../docker-compose.edge-functions.yml \
  up -d --no-deps --force-recreate functions

cd ../../../..
tools/verify_local_delivery_edge_functions.sh
```

The candidate test starts a temporary localhost-only Google and Shopify mock.
It verifies:

- a 15-minute, 10-mile one-way route produces a 30-minute round trip and a
  `$62.40` carrier rate at `$2.08` per minute;
- vendor-specific origins result in two route lookups and four loads totaling
  `$291.20`;
- routes beyond the 50-mile limit return no rate;
- Shopify token exchange and product transformation;
- a deterministic Shopify shipping quote totaling `$172.50`;
- safe handling of a Shopify GraphQL error.

The mock and all disposable origin rows are removed automatically. The
documented acceptance workflow returns the lab to the captured, secret-free
function baseline afterward.

This candidate is not approved for external callback cutover yet. Callback
authentication, real credential injection, public HTTPS, and Shopify callback
registration remain separate gates.
