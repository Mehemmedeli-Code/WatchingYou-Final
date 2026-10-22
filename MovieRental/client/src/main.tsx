import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@/styles/app.css";

import { restoreSession } from "@/lib/api";
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
};

async function bootstrap() {
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

  createRoot(container).render(
    <StrictMode>
      <Island />
    </StrictMode>,
  );
}

void bootstrap();
