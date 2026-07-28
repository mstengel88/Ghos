import { createServer } from "node:http";

const port = Number.parseInt(process.env.PORT ?? "18765", 10);
const host = process.env.HOST ?? "127.0.0.1";
const requests = [];

async function readJson(request) {
  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);
  if (chunks.length === 0) return {};
  return JSON.parse(Buffer.concat(chunks).toString("utf8"));
}

function json(response, status, payload) {
  response.writeHead(status, { "Content-Type": "application/json" });
  response.end(JSON.stringify(payload));
}

const server = createServer(async (request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);
  requests.push({
    method: request.method,
    pathname: url.pathname,
    search: url.search,
  });

  if (url.pathname === "/health") {
    json(response, 200, { status: "ok" });
    return;
  }

  if (url.pathname === "/requests") {
    json(response, 200, { requests });
    return;
  }

  if (url.pathname === "/reset" && request.method === "POST") {
    requests.length = 0;
    json(response, 200, { reset: true });
    return;
  }

  if (url.pathname === "/maps") {
    const origin = url.searchParams.get("origins") ?? "";
    const destination = url.searchParams.get("destinations") ?? "";

    let durationSeconds = 900;
    let distanceMeters = 16093.4;
    let durationText = "15 mins";

    if (destination.includes("Beyond Limit")) {
      durationSeconds = 3600;
      distanceMeters = 96560.4;
      durationText = "60 mins";
    } else if (origin.includes("Vendor A Origin")) {
      durationSeconds = 1200;
      distanceMeters = 19312.08;
      durationText = "20 mins";
    } else if (origin.includes("Vendor B Origin")) {
      durationSeconds = 600;
      distanceMeters = 8046.7;
      durationText = "10 mins";
    }

    json(response, 200, {
      status: "OK",
      destination_addresses: [
        destination || "123 Main St, Milwaukee, WI 53202, USA",
      ],
      rows: [{
        elements: [{
          status: "OK",
          duration: {
            value: durationSeconds,
            text: durationText,
          },
          distance: {
            value: distanceMeters,
            text: `${(distanceMeters / 1609.34).toFixed(1)} mi`,
          },
        }],
      }],
    });
    return;
  }

  if (
    url.pathname === "/shopify/admin/oauth/access_token" &&
    request.method === "POST"
  ) {
    const body = await readJson(request);
    if (body.client_id === "reject") {
      json(response, 401, { error: "invalid_client" });
      return;
    }
    json(response, 200, {
      access_token: "local-candidate-access-token",
      scope: "read_products,read_locations",
    });
    return;
  }

  if (
    url.pathname === "/shopify/admin/api/2024-10/graphql.json" &&
    request.method === "POST"
  ) {
    const body = await readJson(request);
    const query = body.query ?? "";
    const id = body.variables?.id ?? "";

    if (id.endsWith("/999999")) {
      json(response, 200, {
        errors: [{ message: "Synthetic Shopify GraphQL failure" }],
      });
      return;
    }

    if (query.includes("query VariantVendor")) {
      const vendor = id.endsWith("/222") ? "Vendor B" : "Vendor A";
      json(response, 200, {
        data: {
          productVariant: {
            product: { vendor },
          },
        },
      });
      return;
    }

    if (
      query.includes("query Variant(") &&
      query.includes("inventoryItem")
    ) {
      json(response, 200, {
        data: {
          productVariant: {
            id,
            inventoryItem: {
              measurement: {
                weight: { value: 1000, unit: "POUNDS" },
              },
            },
          },
        },
      });
      return;
    }

    if (query.includes("query Product(")) {
      json(response, 200, {
        data: {
          product: {
            id,
            title: "Local Mock Product",
            vendor: "Vendor A",
            productType: "Aggregate",
            tags: ["local", "candidate"],
            variants: {
              edges: [{
                node: {
                  id: "gid://shopify/ProductVariant/111",
                  title: "Ton",
                  price: "45.00",
                  sku: "LOCAL-111",
                  inventoryItem: {
                    measurement: {
                      weight: { value: 2000, unit: "POUNDS" },
                    },
                  },
                },
              }],
            },
          },
        },
      });
      return;
    }

    if (query.includes("locations(first: 50)")) {
      json(response, 200, {
        data: {
          locations: {
            edges: [{
              node: {
                id: "gid://shopify/Location/321",
                name: "Local Mock Yard",
                address: {
                  address1: "W185 N7487 Narrow Ln",
                  address2: "",
                  city: "Menomonee Falls",
                  province: "WI",
                  zip: "53051",
                  country: "US",
                },
              },
            }],
          },
        },
      });
      return;
    }

    json(response, 200, {
      data: {
        products: {
          pageInfo: { hasNextPage: false },
          edges: [],
        },
      },
    });
    return;
  }

  json(response, 404, { error: "Not found" });
});

server.listen(port, host, () => {
  console.log(`Local Edge Function mock listening on ${host}:${port}`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    server.close(() => process.exit(0));
  });
}
