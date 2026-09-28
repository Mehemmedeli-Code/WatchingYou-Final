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
const respond = (path) =>
    /\/api\/venues/.test(path) ? VENUES
  : /\/api\/movies\/genres/.test(path) ? ["Drama", "Thriller"]
  : /\/api\/movies(\?|$)/.test(path) ? { items: [], page: 1, pageSize: 12, total: 0, totalPages: 0 }
  : /\/api\/bookings\/(mine|pending)/.test(path) ? []
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
    await import(pathToFileURL("../src/MovieRental.Host/wwwroot/app/app.js").href + `?p=${page}`);
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
const pages = only ? [only] : PAGES;
let bad = 0;

for (const page of pages) {
  const result = await check(page);
  const broken = result.blank || result.caught.length > 0;
  if (broken) bad++;
  console.log(
    `${broken ? "FAIL" : "ok  "}  ${page.padEnd(12)} ${
      result.blank ? "rendered nothing" : result.caught[0] ?? result.sample}`,
  );
}

console.log(bad === 0 ? "\nAll islands mounted." : `\n${bad} island(s) broken.`);
process.exit(bad === 0 ? 0 : 1);
