import { createServer } from "node:http";

const port = Number.parseInt(process.env.PORT ?? "18765", 10);
const host = process.env.HOST ?? "127.0.0.1";

const server = createServer((request, response) => {
  const url = new URL(request.url ?? "/", `http://${request.headers.host}`);

  if (url.pathname === "/health") {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ status: "ok" }));
    return;
  }

  if (url.pathname === "/maps") {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({
      status: "OK",
      destination_addresses: [
        "123 Main St, Milwaukee, WI 53202, USA",
      ],
      rows: [{
        elements: [{
          status: "OK",
          duration: {
            value: 900,
            text: "15 mins",
          },
          distance: {
            value: 16093.4,
            text: "10.0 mi",
          },
        }],
      }],
    }));
    return;
  }

  response.writeHead(404, { "Content-Type": "application/json" });
  response.end(JSON.stringify({ error: "Not found" }));
});

server.listen(port, host, () => {
  console.log(`Local Edge Function mock listening on ${host}:${port}`);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    server.close(() => process.exit(0));
  });
}
