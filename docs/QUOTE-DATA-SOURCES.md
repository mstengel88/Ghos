# GHOS quote data sources

The GHOS quote workspace combines Shopify product data with the operational
quote configuration formerly used by Dispatch V2.

## Synchronization order

1. Shopify products and variants are refreshed first.
2. Product and variant calculator metafields are copied into GHOS.
3. Dispatch V2 Supabase quote data is imported.
4. Supabase quote pricing, pickup sources, routing rules, companies, settings,
   and saved quote history are applied.

The import is one-way. GHOS does not write back to Supabase or Shopify.

## Shopify calculator metafields

GHOS reads these product metafields from the `custom` namespace:

- `project_calculator_type`
- `coverage_per_order_unit_sq_ft`
- `calculator_order_unit_label`
- `pieces_per_order_unit`
- `unit_length_inches`
- `unit_height_inches`
- `layers_per_pallet`
- `square_feet_per_layer`
- `pallet_weight_lbs`

All fields except `project_calculator_type` are also read from variants. A
variant value takes priority when a quote or calculator is working with that
specific variant. GHOS also reads `green_hills.price_unit_label`, with the
legacy `$app.price_unit_label` as a fallback.

## Supabase tables

The Dispatch quote synchronization reads:

- `product_source_map`: price, contractor tiers, unit label, pickup vendor,
  image, and calculator metadata when those optional columns exist.
- `dispatch_b2b_companies`: Shopify B2B identity, contacts, contractor tier,
  billing information, tax exemption, catalogs, and payment terms.
- `custom_delivery_quotes`: complete saved quote history, line snapshots,
  delivery result, source breakdown, customer, company, and creator details.
- `shipping_material_rules`: SKU prefix, material name, truck capacity,
  pickup source, active state, and sort order.
- `origin_addresses`: pickup labels, addresses, active state, and default
  origin when supplied.
- `shopify_app_settings`: calculated-rate, test-rate, remote-surcharge, and
  vendor-source behavior.

## Refresh behavior

The quote list and editor automatically refresh stale data. The **Refresh
quote data** action on the quote list forces a Shopify product/metafield
refresh followed by a Supabase quote-data refresh.

## Credentials

Secrets stay only in `/opt/ghos/.env` on the Ubuntu VM:

```dotenv
DISPATCH_QUOTE_SUPABASE_URL=
DISPATCH_QUOTE_SERVICE_ROLE_KEY=
```

They are passed into the GHOS container through Docker Compose and must never
be committed to Git.
