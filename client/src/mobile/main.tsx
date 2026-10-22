// Must stay the first import: it sets the language, the translations and the server address
// that every module below reads when it loads.
import "./boot";

import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "@/styles/app.css";
import { restoreSession } from "@/lib/api";
import MobileApp from "./MobileApp";

async function start() {
  // Refresh first, so the app opens already knowing who is signed in.
  await restoreSession().catch(() => null);
  createRoot(document.getElementById("app")!).render(
    <StrictMode>
      <MobileApp />
    </StrictMode>,
  );
}

void start();
