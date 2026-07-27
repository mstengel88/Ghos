const CACHE_NAME = "ghos-static-v1";
const STATIC_ASSETS = [
    "/offline.html",
    "/manifest.webmanifest",
    "/favicon.png",
    "/app.css",
    "/icons/icon-192.png",
    "/icons/icon-512.png",
    "/icons/icon-maskable-512.png",
    "/icons/apple-touch-icon.png"
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(STATIC_ASSETS)));
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((keys) => Promise.all(
                keys
                    .filter((key) => key !== CACHE_NAME)
                    .map((key) => caches.delete(key))))
            .then(() => self.clients.claim()));
});

self.addEventListener("message", (event) => {
    if (event.data?.type === "SKIP_WAITING") {
        self.skipWaiting();
    }
});

self.addEventListener("fetch", (event) => {
    const request = event.request;
    if (request.method !== "GET") {
        return;
    }

    const url = new URL(request.url);
    if (url.origin !== self.location.origin) {
        return;
    }

    if (request.mode === "navigate") {
        event.respondWith(
            fetch(request).catch(() => caches.match("/offline.html")));
        return;
    }

    const isSafeStaticAsset =
        url.pathname === "/favicon.png" ||
        url.pathname === "/app.css" ||
        url.pathname === "/manifest.webmanifest" ||
        url.pathname === "/offline.html" ||
        url.pathname.startsWith("/icons/");

    if (!isSafeStaticAsset) {
        return;
    }

    event.respondWith(
        caches.match(request).then((cached) => {
            const fresh = fetch(request)
                .then((response) => {
                    if (response.ok) {
                        const copy = response.clone();
                        caches.open(CACHE_NAME)
                            .then((cache) => cache.put(request, copy));
                    }
                    return response;
                })
                .catch(() => cached);
            return cached ?? fresh;
        }));
});
