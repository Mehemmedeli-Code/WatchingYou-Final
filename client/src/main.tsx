import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@/styles/app.css";
import { t } from "@/lib/i18n";

import { restoreSession } from "@/lib/api";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { ShaderBackground } from "@/components/ui/shader-background";
import { clearOfflineTickets } from "@/lib/offlineTickets";
import { auth, get } from "@/lib/api";
import type { ComponentType } from "react";

/**
 * Island mounting. Razor owns routing and the page shell; each page declares which React
 * component belongs in its #root via data-page.
 *
 * Each island is its own chunk, loaded only on the page that uses it. With every page in one
 * bundle, opening the catalogue meant downloading and parsing the back office, the admin
 * screens and the studio as well — most of the wait on every page.
 */
type IslandModule = { default: ComponentType };

/** A brief hiccup (the server restarting, a flaky connection) should cost a second, not the page. */
async function withRetry<T>(load: () => Promise<T>, attempts = 4): Promise<T> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await load();
    } catch (err) {
      if (attempt >= attempts) throw err;
      await new Promise((resolve) => setTimeout(resolve, 800 * attempt));
    }
  }
}
const ISLANDS: Record<string, () => Promise<IslandModule>> = {
  home: () => import("@/pages/HomePage"),
  cinema: () => import("@/pages/CinemaPage"),
  rentals: () => import("@/pages/RentalsPage"),
  studio: () => import("@/pages/StudioPage"),
  admin: () => import("@/pages/AdminPage"),
  account: () => import("@/pages/AccountPage"),
  onDisplay: () => import("@/pages/OnDisplayPage"),
  aiCatalog: () => import("@/pages/GalleryPage"),
  humanCraft: () => import("@/pages/HumanCraftPage"),
  security: () => import("@/pages/SecurityPage"),
  globe: () => import("@/pages/GlobePage"),
  people: () => import("@/pages/PeoplePage"),
  profile: () => import("@/pages/ProfilePage"),
  profileEdit: () => import("@/pages/ProfileEditPage"),
  help: () => import("@/pages/HelpPage"),
  backoffice: () => import("@/backoffice/BackOfficeApp"),
  favourites: () => import("@/pages/FavouritesPage"),
  pro: () => import("@/pages/ProPage"),
};

async function bootstrap() {
  // Rendered first and separately: the backdrop should appear immediately rather than
  // waiting on the session refresh the page island needs.
  const shader = document.getElementById("rr-shader");
  if (shader) {
    createRoot(shader).render(<ShaderBackground className="h-full w-full" />);
  }

  // Its own root, like the shader: the header is Razor, so the switcher mounts into a slot
  // there rather than inside any page's island.
  const themeSlot = document.getElementById("rr-theme");
  if (themeSlot) {
    createRoot(themeSlot).render(<ThemeSwitcher />);
  }

  const container = document.getElementById("root");
  if (!container) return;

  const name = container.dataset.page ?? "home";
  const loadIsland = ISLANDS[name];

  if (!loadIsland) {
    console.warn(`No React island is registered for data-page="${name}".`);
    return;
  }

  // The page's code and the session refresh travel at the same time; the page then renders
  // once, already knowing who is signed in. (One after the other used to add a round trip.)
  let module: IslandModule;
  try {
    [module] = await Promise.all([
      withRetry(loadIsland),
      restoreSession().catch(() => null),
    ]);
  } catch {
    // The server could not be reached even after retrying: say so, with a way to try again,
    // instead of leaving an empty page.
    container.innerHTML = `<div style="max-width:560px;margin:80px auto;padding:24px;text-align:center;font-family:Inter,sans-serif">
      <p style="font-size:18px;margin:0 0 16px">${t("common.loadFailed", "The page could not be loaded. Check your connection.")}</p>
      <button type="button" data-reload style="background:#22E07A;color:#03140A;border:0;border-radius:999px;padding:10px 22px;font-weight:600;cursor:pointer">${t("common.reload", "Reload")}</button></div>`;
    // A listener, not onclick="": the Content-Security-Policy runs no inline handlers.
    container.querySelector("[data-reload]")?.addEventListener("click", () => location.reload());
    return;
  }
  const Island = module.default;

  // Site-wide message pop-ups: their own root, appended to <body>, so no Razor page has to
  // make room for them. Mounted after the session refresh so the socket starts signed in.
  // Loaded on demand and only when signed in: it brings SignalR, motion and the chat window,
  // which used to sit in app.js and be parsed before every page's own island.
  if (auth.user) {
    // Follow requests waiting: a count on the header picture, so nobody has to go looking.
    void get<{ requests: number }>("/api/people/me").then(({ requests }) => {
      const avatar = document.querySelector<HTMLElement>(".rr-avatar");
      if (avatar && requests > 0) avatar.dataset.count = requests > 9 ? "9+" : String(requests);
    }).catch(() => null);

    void import("@/components/LiveNotifications").then(({ LiveNotifications }) => {
      const liveSlot = document.createElement("div");
      liveSlot.id = "rr-live";
      document.body.appendChild(liveSlot);
      createRoot(liveSlot).render(<ErrorBoundary label="live"><LiveNotifications /></ErrorBoundary>);
    });
  }
  auth.subscribe((user) => {
    if (user) return;
    // Signed out: close the socket and forget anything this device kept for that account.
    // Lazy: SignalR is only needed by signed-in pages, so it stays out of app.js.
    void import("@/lib/realtime").then(({ stopRealtime }) => stopRealtime());
    clearOfflineTickets();
    navigator.serviceWorker?.controller?.postMessage("clear-pages");
  });

  createRoot(container).render(
    <StrictMode>
      <ErrorBoundary label={name}>
        <Island />
      </ErrorBoundary>
    </StrictMode>,
  );
}

void bootstrap();

// Installable app + offline shell. Skipped on the Vite dev server, where a worker caching the
// bundle would only hide the change you just made.
if ("serviceWorker" in navigator && window.location.port !== "5173") {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => undefined);
  });
}
