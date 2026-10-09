/**
 * Runs before any other module of the phone app.
 *
 * The website gets its language and translations inlined into the page by Razor
 * (window.__RR__). The app has no server-rendered page, so it bundles the same four locale
 * files and sets the same global itself — which is why every shared component translates
 * unchanged. It also announces the app (window.__WY_APP__), which lib/platform.ts reads to
 * send API calls to the real server and keep navigation inside the app.
 */
import az from "../../../src/MovieRental.Host/locales/az.json";
import en from "../../../src/MovieRental.Host/locales/en.json";
import ru from "../../../src/MovieRental.Host/locales/ru.json";
import tr from "../../../src/MovieRental.Host/locales/tr.json";

declare const __WY_API_BASE__: string;

export const LANGUAGES = ["az", "en", "ru", "tr"] as const;
export type AppLanguage = (typeof LANGUAGES)[number];

const LANG_KEY = "wy.app.lang";
const API_KEY = "wy.app.apiBase";
const dictionaries: Record<AppLanguage, Record<string, string>> = { az, en, ru, tr };

function read(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}

const saved = read(LANG_KEY) as AppLanguage | null;
export const appLanguage: AppLanguage = saved && LANGUAGES.includes(saved) ? saved : "az";

export function setAppLanguage(code: AppLanguage) {
  try { localStorage.setItem(LANG_KEY, code); } catch { /* keeps the current language */ }
  window.location.reload();
}

(window as unknown as { __RR__: unknown }).__RR__ = { lang: appLanguage, t: dictionaries[appLanguage] };
document.documentElement.lang = appLanguage;

// The server the app talks to. Fixed at build time (WY_API_BASE); a tester can point a build
// at another server by setting "wy.app.apiBase" in storage, without rebuilding.
const apiBase = (read(API_KEY) || __WY_API_BASE__ || "").replace(/\/+$/, "");

(window as unknown as { __WY_APP__: unknown }).__WY_APP__ = {
  apiBase,
  navigate: (route: string) => { window.location.hash = `#/${route}`; },
};

// The server hands out its pictures and videos (posters, avatars, short films) as paths on
// itself — "/api/posters/…". On the website that is the page's own origin; in the app the page
// is on the device, so those paths are pointed at the server as they appear.
if (apiBase) {
  const fix = (el: Element) => {
    const src = el.getAttribute("src");
    if (src && src.startsWith("/api/")) el.setAttribute("src", apiBase + src);
  };
  const scan = (root: ParentNode) => root.querySelectorAll("img[src^='/api/'], video[src^='/api/'], source[src^='/api/']").forEach(fix);
  new MutationObserver((changes) => {
    for (const change of changes) {
      if (change.type === "attributes") fix(change.target as Element);
      else change.addedNodes.forEach((node) => {
        if (node instanceof Element) { fix(node); scan(node); }
      });
    }
  }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ["src"] });
}

// Same theme handling as the website, before first paint.
try {
  const theme = read("wy.theme") === "light" ? "light" : "dark";
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme;
} catch { /* stays dark */ }
