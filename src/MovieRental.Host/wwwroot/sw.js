/*
 * WatchingYou service worker.
 *
 * What it does, and deliberately no more:
 *  - Keeps the app shell (bundle, styles, icons, an offline page) so the site opens without a
 *    network, and pages you have visited open from cache when the network is gone.
 *  - Never caches /api/ or /hubs/. Personal data does not go into Cache Storage; the tickets a
 *    customer needs offline are kept by the page itself (see lib/offlineTickets.ts) and wiped
 *    at sign-out.
 *
 * Bump VERSION when this file changes so old caches are cleared on activation.
 */
const VERSION = "wy-v1";
const SHELL = `${VERSION}-shell`;
const PAGES = `${VERSION}-pages`;

const PRECACHE = [
  "/offline.html",
  "/app/app.js",
  "/app/app.css",
  "/css/shell.css",
  "/icons/icon-192.png",
  "/icons/icon.svg",
  "/manifest.webmanifest",
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(SHELL)
      // One missing file must not stop the worker installing; the rest still help.
      .then((cache) => Promise.all(PRECACHE.map((url) => cache.add(url).catch(() => undefined))))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys()
      .then((keys) => Promise.all(keys.filter((k) => !k.startsWith(VERSION)).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("message", (event) => {
  // Sent by the page at sign-out: forget every cached page, in case one showed a name.
  if (event.data === "clear-pages") event.waitUntil(caches.delete(PAGES));
});

self.addEventListener("fetch", (event) => {
  const request = event.request;
  if (request.method !== "GET") return;

  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return;
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/hubs/") ||
      url.pathname.startsWith("/swagger") || url.pathname.startsWith("/scalar")) return;

  // Pages: network first, so a signed-in page is always fresh; the cached copy and then the
  // offline page are only for when the network is gone.
  if (request.mode === "navigate") {
    event.respondWith(
      fetch(request)
        .then((response) => {
          if (response.ok && response.type === "basic") {
            const copy = response.clone();
            caches.open(PAGES).then((cache) => cache.put(request, copy));
          }
          return response;
        })
        .catch(async () =>
          (await caches.match(request, { ignoreSearch: true })) ??
          (await caches.match("/offline.html")) ??
          new Response("Offline", { status: 503, headers: { "Content-Type": "text/plain" } })),
    );
    return;
  }

  // Static files: answer from cache at once, refresh it in the background.
  if (/^\/(app|css|icons|img|seed)\//.test(url.pathname) || url.pathname === "/manifest.webmanifest") {
    event.respondWith(
      caches.open(SHELL).then(async (cache) => {
        const cached = await cache.match(request);
        const network = fetch(request)
          .then((response) => {
            if (response.ok) cache.put(request, response.clone());
            return response;
          })
          .catch(() => cached);
        return cached ?? network;
      }),
    );
  }
});
