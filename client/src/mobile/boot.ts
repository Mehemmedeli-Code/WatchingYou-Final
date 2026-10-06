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

// Same theme handling as the website, before first paint.
try {
  const theme = read("wy.theme") === "light" ? "light" : "dark";
  document.documentElement.dataset.theme = theme;
  document.documentElement.style.colorScheme = theme;
} catch { /* stays dark */ }
