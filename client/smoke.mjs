/**
 * Mounts every React island in a fake DOM and reports anything that throws.
 *
 * This exists because a single component that failed to start left the whole page blank with
 * no message. `tsc` and `vite build` both passed — nothing catches a runtime crash but
 * running the thing. jsdom has no WebGL, which makes it a good stand-in for the machines
 * where that actually happens.
 *
 *   npm run smoke           # every page
 *   npm run smoke -- globe  # one page
 */
import { JSDOM } from "jsdom";
import { pathToFileURL } from "node:url";

const PAGES = ["home", "onDisplay", "cinema", "rentals", "studio", "admin", "account",
               "aiCatalog", "humanCraft", "security", "globe", "help"];

const VENUES = [{
  id: "11111111-1111-1111-1111-111111111111", name: "Nizami", city: "Baku",
  address: "28 May 1", latitude: 40.3725, longitude: 49.8375,
  halls: [{ id: "22222222-2222-2222-2222-222222222222", name: "A100" }],
}];

// Response shapes matter: /api/movies is paged, /api/movies/genres is a list. Getting these
// wrong makes the harness report bugs the app does not have.
// SMOKE_USER=admin mounts every page signed in as an admin, which is where most of the
// screens (tickets, loyalty, watchlist, admin tools) actually render.
const SIGNED_IN = process.env.SMOKE_USER === "admin";
const fakeJwt = () => {
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString("base64url");
  return `${b64({ alg: "none" })}.${b64({ exp: Math.floor(Date.now() / 1000) + 3600 })}.x`;
};
const AUTH = {
  accessToken: fakeJwt(), accessTokenExpiresAtUtc: new Date(Date.now() + 3600_000).toISOString(), refreshToken: "r",
  user: { id: "33333333-3333-3333-3333-333333333333", fullName: "Smoke Admin", email: "admin@test",
          isEmailConfirmed: true, isPhoneConfirmed: false, roles: ["Admin", "Security"] },
};

const respond = (path) =>
    /\/api\/auth\/refresh/.test(path) ? AUTH
  : /\/api\/admin\/analytics\/cinema-totals/.test(path) ? { revenueThisMonth: 120, ticketsThisMonth: 14, upcomingScreenings: 9, activePromoCodes: 2 }
  : /\/api\/admin\/rentals\/stats/.test(path) ? { activeCount: 1, overdueCount: 0, returnedCount: 3, outstandingLateFees: 0, revenueThisMonth: 12 }
  : /\/api\/admin\/analytics\/overview/.test(path) ? [{ key: "cinema-revenue", title: "Ticket revenue", unit: "AZN", caption: "", points: [{ label: "25.09", value: 12.5 }] }]
  : /\/api\/screenings\/[^/]+\/quote/.test(path) ? { subtotal: 17, promoDiscount: 0, promoApplied: false, pointsBalance: 40, pointsUsed: 0, pointsDiscount: 0, total: 17, pointsEarned: 17, stripeEnabled: true }
  : /\/api\/rentals\/mine/.test(path) ? { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }
  : /\/api\/help\/chat/.test(path) ? null
  : /\/api\/(reports|admin\/rentals\/overdue|help\/inbox)/.test(path) ? []
  : /\/api\/venues/.test(path) ? VENUES
  : /\/api\/movies\/genres/.test(path) ? ["Drama", "Thriller"]
  : /\/api\/movies(\?|$)/.test(path) ? { items: [], page: 1, pageSize: 12, total: 0, totalPages: 0 }
  : /\/api\/bookings\/(mine|pending)/.test(path) ? []
  : /\/api\/recommendations/.test(path) ? { personal: false, items: [] }
  : /\/api\/(watchlist|messages|admin\/promos)/.test(path) ? []
  : /\/api\/loyalty/.test(path) ? { balance: 0, held: 0, spendable: 0, manatPerPoint: 0.05, pointsPerManat: 1, maxShare: 0.5, history: [] }
  : /\/api\/globe\/members/.test(path) ? { city: "Baku", total: 0, page: 1, pageSize: 24, items: [] }
  : /\/(screenings|shorts|cities|audit|users|on-display|gallery|analytics|queue|help)/.test(path) ? []
  : {};

async function check(page) {
  const dom = new JSDOM(
    `<!doctype html><html><body>
       <section id="root" data-page="${page}"></section>
       <script id="rr-i18n" type="application/json">{}</script>
     </body></html>`,
    { url: "https://localhost:7139/", pretendToBeVisual: true },
  );

  const { window } = dom;
  window.__RR__ = { lang: "en", t: {} };
  window.matchMedia = (query) => ({
    media: query, matches: false, onchange: null,
    addEventListener() {}, removeEventListener() {},
    addListener() {}, removeListener() {}, dispatchEvent() { return false; },
  });
  window.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  window.IntersectionObserver = class { observe() {} unobserve() {} disconnect() {} };
  window.scrollTo = () => {};
  if (SIGNED_IN) window.localStorage.setItem("rr.refresh", "r");

  for (const key of ["window", "document", "navigator", "location", "HTMLElement", "Element",
                     "Node", "customElements", "getComputedStyle", "requestAnimationFrame",
                     "cancelAnimationFrame", "MutationObserver", "ResizeObserver",
                     "IntersectionObserver", "matchMedia", "localStorage", "sessionStorage",
                     "Image", "SVGElement", "DOMParser", "Event", "CustomEvent"]) {
    if (window[key] === undefined) continue;
    try {
      Object.defineProperty(globalThis, key, { value: window[key], configurable: true, writable: true });
    } catch { /* a few globals are read-only in Node; the bundle does not need those */ }
  }
  globalThis.self = window;
  globalThis.fetch = async (url) => ({
    ok: true, status: 200, json: async () => respond(String(url)), headers: new Map(),
  });

  const failures = [];
  const realError = console.error;
  console.error = (...args) => { failures.push(args.map(String).join(" ")); };

  try {
    await import(pathToFileURL("../src/MovieRental.Host/wwwroot/app/app.js").href);
    await new Promise((r) => setTimeout(r, 2500));
  } catch (error) {
    failures.push(`threw during module evaluation: ${error?.message ?? error}`);
  } finally {
    console.error = realError;
  }

  const text = (window.document.getElementById("root").textContent ?? "").replace(/\s+/g, " ").trim();
  const blank = text.length === 0;
  const caught = failures.filter((f) => f.includes("failed:") || f.includes("threw"));

  return { page, blank, caught, sample: text.slice(0, 70) };
}

const only = process.argv[2];

if (only) {
  // One page, one process, one copy of the bundle — the way a browser loads it.
  const result = await check(only);
  const broken = result.blank || result.caught.length > 0;
  console.log(
    `${broken ? "FAIL" : "ok  "}  ${only.padEnd(12)} ${
      result.blank ? "rendered nothing" : result.caught[0] ?? result.sample}`,
  );
  process.exit(broken ? 1 : 0);
}

const { spawnSync } = await import("node:child_process");
const { fileURLToPath } = await import("node:url");
let bad = 0;

for (const page of PAGES) {
  const run = spawnSync(process.execPath, [fileURLToPath(import.meta.url), page], { encoding: "utf8" });
  const line = (run.stdout || "").trim().split("\n").pop() || `FAIL  ${page} produced no output`;
  if (run.status !== 0) bad++;
  console.log(line);
}

console.log(bad === 0 ? "\nAll islands mounted." : `\n${bad} island(s) broken.`);
process.exit(bad === 0 ? 0 : 1);
