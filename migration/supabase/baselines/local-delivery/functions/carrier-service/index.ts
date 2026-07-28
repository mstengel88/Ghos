import { serve } from "https://deno.land/std@0.168.0/http/server.ts";
import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type, x-supabase-client-platform, x-supabase-client-platform-version, x-supabase-client-runtime, x-supabase-client-runtime-version",
};

const supabaseAdmin = createClient(
  Deno.env.get("SUPABASE_URL")!,
  Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!
);

// Settings loaded from DB at request time
async function getSettings(): Promise<Record<string, string>> {
  const { data } = await supabaseAdmin
    .from("app_settings")
    .select("key, value")
    .in("key", ["max_qty_per_truck", "rate_per_minute", "max_miles", "default_origin"]);
  const map: Record<string, string> = {};
  for (const r of data || []) map[r.key] = r.value;
  return map;
}

async function getActiveOriginAddress(fallback: string): Promise<string> {
  const { data } = await supabaseAdmin
    .from("origin_addresses")
    .select("address")
    .eq("is_active", true)
    .limit(1)
    .single();
  return data?.address || fallback;
}

async function getShopifyAccessToken(): Promise<string | null> {
  const domain = Deno.env.get("SHOPIFY_STORE_DOMAIN")?.replace(/^https?:\/\//, "").replace(/\/.*$/, "").trim();
  const clientId = Deno.env.get("SHOPIFY_CLIENT_ID");
  const clientSecret = Deno.env.get("SHOPIFY_CLIENT_SECRET");
  if (!domain || !clientId || !clientSecret) return null;

  try {
    const tokenRes = await fetch(`https://${domain}/admin/oauth/access_token`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ client_id: clientId, client_secret: clientSecret, grant_type: "client_credentials" }),
    });
    if (!tokenRes.ok) return null;
    const tokenData = await tokenRes.json();
    return tokenData.access_token;
  } catch {
    return null;
  }
}

async function getOriginFromProductVendor(variantId: number): Promise<string | null> {
  const domain = Deno.env.get("SHOPIFY_STORE_DOMAIN")?.replace(/^https?:\/\//, "").replace(/\/.*$/, "").trim();
  const accessToken = await getShopifyAccessToken();
  if (!accessToken || !domain) return null;

  try {
    const gid = `gid://shopify/ProductVariant/${variantId}`;
    const res = await fetch(`https://${domain}/admin/api/2024-10/graphql.json`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Shopify-Access-Token": accessToken,
      },
      body: JSON.stringify({
        query: `query VariantVendor($id: ID!) {
          productVariant(id: $id) {
            product {
              vendor
            }
          }
        }`,
        variables: { id: gid },
      }),
    });

    if (!res.ok) return null;
    const json = await res.json();
    const vendor = json.data?.productVariant?.product?.vendor;
    if (!vendor) return null;

    console.log(`Resolved vendor for variant ${variantId}: ${vendor}`);

    const { data } = await supabaseAdmin
      .from("origin_addresses")
      .select("address")
      .ilike("label", vendor)
      .limit(1)
      .single();

    if (data?.address) {
      console.log(`Matched vendor "${vendor}" to origin address: ${data.address}`);
      return data.address;
    }

    console.log(`No origin address found matching vendor "${vendor}"`);
    return null;
  } catch (err) {
    console.error("Failed to fetch vendor origin:", err);
    return null;
  }
}

async function getDriveTimeCost(
  originAddress: string,
  destinationAddress: string,
  googleMapsApiKey: string
): Promise<{ costDollars: number; oneWayMiles: number; durationText: string; roundTripMinutes: number } | null> {
  const mapsUrl = new URL("https://maps.googleapis.com/maps/api/distancematrix/json");
  mapsUrl.searchParams.set("origins", originAddress);
  mapsUrl.searchParams.set("destinations", destinationAddress);
  mapsUrl.searchParams.set("key", googleMapsApiKey);
  mapsUrl.searchParams.set("units", "imperial");

  const mapsRes = await fetch(mapsUrl.toString());
  const mapsData = await mapsRes.json();

  if (mapsData.status !== "OK") {
    console.error("Google Maps error:", mapsData.status, mapsData.error_message);
    return null;
  }

  const element = mapsData.rows?.[0]?.elements?.[0];
  if (!element || element.status !== "OK") {
    console.error("No route found:", element?.status);
    return null;
  }

  const oneWaySeconds = element.duration.value;
  const roundTripMinutes = (oneWaySeconds * 2) / 60;
  const costDollars = roundTripMinutes * RATE_PER_MINUTE;
  const oneWayMiles = Math.round(element.distance.value / 1609.34 * 10) / 10;

  return { costDollars, oneWayMiles, durationText: element.duration.text, roundTripMinutes };
}

serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response(null, { headers: corsHeaders });
  }

  if (req.method !== "POST") {
    return new Response(JSON.stringify({ rates: [] }), {
      status: 200,
      headers: { ...corsHeaders, "Content-Type": "application/json" },
    });
  }

  try {
    const body = await req.json();
    const rateRequest = body.rate;

    if (!rateRequest) {
      console.error("No rate object in request body");
      return new Response(JSON.stringify({ rates: [] }), {
        status: 200,
        headers: { ...corsHeaders, "Content-Type": "application/json" },
      });
    }

    const dest = rateRequest.destination;
    if (!dest) {
      console.error("No destination in rate request");
      return new Response(JSON.stringify({ rates: [] }), {
        status: 200,
        headers: { ...corsHeaders, "Content-Type": "application/json" },
      });
    }

    const destParts = [
      dest.address1, dest.address2, dest.city,
      dest.province, dest.postal_code, dest.country,
    ].filter(Boolean);
    const destinationAddress = destParts.join(", ");

    console.log("Carrier service request for destination:", destinationAddress);

    const GOOGLE_MAPS_API_KEY = Deno.env.get("GOOGLE_MAPS_API_KEY");
    if (!GOOGLE_MAPS_API_KEY) {
      console.error("GOOGLE_MAPS_API_KEY not configured");
      return new Response(JSON.stringify({ rates: [] }), {
        status: 200,
        headers: { ...corsHeaders, "Content-Type": "application/json" },
      });
    }

    const items = rateRequest.items || [];
    const settings = await getSettings();
    const MAX_QTY_PER_TRUCK = parseInt(settings["max_qty_per_truck"] || "22", 10);
    const RATE_PER_MINUTE = parseFloat(settings["rate_per_minute"] || "2.08");
    const MAX_MILES = parseFloat(settings["max_miles"] || "50");
    const DEFAULT_ORIGIN_FALLBACK = settings["default_origin"] || "W185 N7487, Narrow Ln, Menomonee Falls, WI 53051";
    const defaultOrigin = await getActiveOriginAddress(DEFAULT_ORIGIN_FALLBACK);

    // Group items by their origin address (vendor-based)
    // Each unique line item = 1 delivery, extra trucks if weight > 22 tons
    let totalDeliveryCostCents = 0;
    let totalTrucks = 0;
    const routeCache: Record<string, { costDollars: number; oneWayMiles: number; durationText: string; roundTripMinutes: number }> = {};

    for (const item of items) {
      // Resolve origin for this item
      let origin: string | null = null;
      if (item.variant_id) {
        origin = await getOriginFromProductVendor(item.variant_id);
      }
      if (!origin) {
        origin = defaultOrigin;
      }

      console.log(`Item variant ${item.variant_id}: origin=${origin}`);

      // How many trucks needed for this line item based on quantity
      const itemQty = item.quantity || 1;
      const trucksForItem = Math.max(1, Math.ceil(itemQty / MAX_QTY_PER_TRUCK));

      console.log(`Item variant ${item.variant_id}: qty=${itemQty}, ${trucksForItem} truck(s) needed`);

      // Get route cost (cache by origin to avoid duplicate Google Maps calls)
      const cacheKey = `${origin}|${destinationAddress}`;
      let routeCost = routeCache[cacheKey];
      if (!routeCost) {
        const result = await getDriveTimeCost(origin, destinationAddress, GOOGLE_MAPS_API_KEY);
        if (!result) {
          console.error(`No route for item variant ${item.variant_id}`);
          continue;
        }
        routeCache[cacheKey] = result;
        routeCost = result;
      }

      // Check mileage limit — if ANY item is beyond 50 miles, return no rates
      if (routeCost.oneWayMiles > MAX_MILES) {
        console.log(`Item variant ${item.variant_id}: ${routeCost.oneWayMiles} miles exceeds ${MAX_MILES} mile limit — returning no rates`);
        return new Response(JSON.stringify({ rates: [] }), {
          status: 200,
          headers: { ...corsHeaders, "Content-Type": "application/json" },
        });
      }

      const itemDeliveryCost = routeCost.costDollars * trucksForItem;
      totalDeliveryCostCents += Math.round(itemDeliveryCost * 100);
      totalTrucks += trucksForItem;

      console.log(`Item variant ${item.variant_id}: ${trucksForItem} truck(s) × $${routeCost.costDollars.toFixed(2)} = $${itemDeliveryCost.toFixed(2)}`);
    }

    // Fallback: if no items processed, calculate single delivery from default origin
    if (totalTrucks === 0) {
      const result = await getDriveTimeCost(defaultOrigin, destinationAddress, GOOGLE_MAPS_API_KEY);
      if (result) {
        totalDeliveryCostCents = Math.round(result.costDollars * 100);
        totalTrucks = 1;
      }
    }

    console.log(`Total: ${totalTrucks} truck(s), $${(totalDeliveryCostCents / 100).toFixed(2)}`);

    const description = totalTrucks > 1
      ? `Delivery (${totalTrucks} loads required)`
      : "GHS Delivery";

    const rates = [
      {
        service_name: "GHS Delivery",
        service_code: "ghs_delivery",
        total_price: String(totalDeliveryCostCents),
        description,
        currency: rateRequest.currency || "USD",
        min_delivery_date: null,
        max_delivery_date: null,
      },
    ];

    return new Response(JSON.stringify({ rates }), {
      status: 200,
      headers: { ...corsHeaders, "Content-Type": "application/json" },
    });
  } catch (error) {
    console.error("Carrier service error:", error);
    return new Response(JSON.stringify({ rates: [] }), {
      status: 200,
      headers: { ...corsHeaders, "Content-Type": "application/json" },
    });
  }
});
