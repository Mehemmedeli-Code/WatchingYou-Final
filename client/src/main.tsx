import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@/styles/app.css";

import { restoreSession } from "@/lib/api";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { ThemeSwitcher } from "@/components/ui/theme-switcher";
import { ShaderBackground } from "@/components/ui/shader-background";
import { LiveNotifications } from "@/components/LiveNotifications";
import { stopRealtime } from "@/lib/realtime";
import { clearOfflineTickets } from "@/lib/offlineTickets";
import { auth } from "@/lib/api";
import HomePage from "@/pages/HomePage";
import CinemaPage from "@/pages/CinemaPage";
import RentalsPage from "@/pages/RentalsPage";
import StudioPage from "@/pages/StudioPage";
import AdminPage from "@/pages/AdminPage";
import AccountPage from "@/pages/AccountPage";
import OnDisplayPage from "@/pages/OnDisplayPage";
import AiCatalogPage from "@/pages/GalleryPage";
import HumanCraftPage from "@/pages/HumanCraftPage";
import SecurityPage from "@/pages/SecurityPage";
import GlobePage from "@/pages/GlobePage";
import HelpPage from "@/pages/HelpPage";

/**
 * Island mounting. Razor owns routing and the page shell; each page declares which React
 * component belongs in its #root via data-page. One bundle, ten entry points, no client
 * router fighting the server for the URL.
 */
const ISLANDS: Record<string, () => JSX.Element> = {
  home: HomePage,
  cinema: CinemaPage,
  rentals: RentalsPage,
  studio: StudioPage,
  admin: AdminPage,
  account: AccountPage,
  onDisplay: OnDisplayPage,
  aiCatalog: AiCatalogPage,
  humanCraft: HumanCraftPage,
  security: SecurityPage,
  globe: GlobePage,
  help: HelpPage,
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
  const Island = ISLANDS[name];

  if (!Island) {
    console.warn(`No React island is registered for data-page="${name}".`);
    return;
  }

  // Refresh first: the page then renders once, already knowing who is signed in.
  await restoreSession().catch(() => null);

  // Site-wide message pop-ups: their own root, appended to <body>, so no Razor page has to
  // make room for them. Mounted after the session refresh so the socket starts signed in.
  const liveSlot = document.createElement("div");
  liveSlot.id = "rr-live";
  document.body.appendChild(liveSlot);
  createRoot(liveSlot).render(<ErrorBoundary label="live"><LiveNotifications /></ErrorBoundary>);
  auth.subscribe((user) => {
    if (user) return;
    // Signed out: close the socket and forget anything this device kept for that account.
    stopRealtime();
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
