# Calculator source of truth

GHOS, the quote workspace, and the Shopify product-page calculators use the
same Shopify product and variant metafields.

## Shared metafields

- `custom.project_calculator_type`: `paver` or `wall`
- `custom.coverage_per_order_unit_sq_ft`
- `custom.calculator_order_unit_label`
- `custom.pieces_per_order_unit`
- `custom.unit_length_inches`
- `custom.unit_height_inches`
- `custom.layers_per_pallet`
- `custom.square_feet_per_layer`
- `custom.pallet_weight_lbs`

Variant values override product values. GHOS imports these fields during its
Shopify product synchronization and refreshes stale Shopify data when either
Project Calculators or the quote editor opens.

GHOS follows the Shopify County calculator’s product recognition rules. It uses
`custom.project_calculator_type` first. When that field is blank, a title,
product type, or tag containing `wall` or `tribute` selects the wall calculator;
`paver` or `discover` selects the paver calculator. Keep installation
accessories off the County product template or set their product data so they do
not match those fallback terms.

The shared server-side calculation rules live in:

- `ProjectTools/MaterialCalculator.cs` for bulk materials.
- `ProjectTools/PaverWallCalculator.cs` for pavers and walls.

Both the standalone GHOS calculators and the calculator embedded in each quote
line call these same classes. Do not copy calculation math into a Razor page.
Both support measured dimensions or a known square-foot area for paver and wall
projects, matching the Shopify County calculator.

## Confirmed fallbacks

- Standard Discover Pavers: 12.45 sq. ft. per layer, 8 layers per pallet,
  99.60 sq. ft. per pallet, and 3,175 lb. per pallet.
- Tribute wall: 16-inch face length and 6-inch height when the corresponding
  metafields are blank.

The Shopify Custom Liquid implementation remains responsible for cart actions,
but its quantities use the same metafields and confirmed fallback values.
