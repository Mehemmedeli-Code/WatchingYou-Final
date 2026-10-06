import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { renameSync, existsSync } from "node:fs";
import { fileURLToPath, URL } from "node:url";

// The phone app's build. Same source as the website, a different entry: mobile.html boots a
// small single-page app (Films / My tickets / Account) that Capacitor wraps for iOS and
// Android. It lands in dist-mobile, which capacitor.config.ts points at.
//
// The server the app talks to is fixed at build time:
//   WY_API_BASE=https://api.watchingyou.az npm run build:mobile

const outDir = fileURLToPath(new URL("./dist-mobile", import.meta.url));

/** Capacitor opens index.html; Vite names the page after its source file. */
const asIndex = (): Plugin => ({
  name: "mobile-as-index",
  closeBundle() {
    const from = `${outDir}/mobile.html`;
    if (existsSync(from)) renameSync(from, `${outDir}/index.html`);
  },
});

export default defineConfig({
  plugins: [react(), tailwindcss(), asIndex()],
  // Relative asset paths: inside the app the page is loaded from the device, not a web root.
  base: "./",
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  define: {
    __WY_API_BASE__: JSON.stringify(process.env.WY_API_BASE ?? "https://localhost:7139"),
  },
  server: { port: 5174, strictPort: true },
  build: {
    outDir,
    emptyOutDir: true,
    rollupOptions: {
      input: fileURLToPath(new URL("./mobile.html", import.meta.url)),
    },
  },
});
