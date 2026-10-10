import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { fileURLToPath, URL } from "node:url";

// The Razor host is the page; Vite only produces the island bundle.
// Fixed output filenames mean _Layout.cshtml can hard-code the script tag instead of
// reading a manifest at request time.
export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss()],
  // The built bundle is served from /app/. Without this Vite assumed the site root: lazily
  // loaded chunks still worked (they are imported by relative path), but their stylesheets
  // were preloaded from "/CinemaMapInner.css" — a 404 that made the cinema map fail with
  // "Unable to preload CSS". The dev server keeps the root, where Razor looks for it.
  base: command === "build" ? "/app/" : "/",
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    cors: true,
    // API calls from the dev server hit the .NET host directly.
    proxy: {
      "/api": {
        target: "https://localhost:7139",
        changeOrigin: true,
        secure: false,
      },
      // The SignalR hub: negotiate over HTTP, then upgrade to a WebSocket.
      "/hubs": {
        target: "https://localhost:7139",
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
  build: {
    outDir: "../src/MovieRental.Host/wwwroot/app",
    emptyOutDir: true,
    manifest: false,
    rolldownOptions: {
      input: fileURLToPath(new URL("./src/main.tsx", import.meta.url)),
      output: {
        entryFileNames: "app.js",
        // Everything app.js loads carries a content hash, so the host can cache it for a
        // year (see Program.cs); only app.js/app.css are revalidated on each load.
        chunkFileNames: "[name]-[hash].js",
        // The entry stylesheet keeps its fixed name so _Layout.cshtml can hard-code it.
        // A lazily loaded chunk brings its own CSS — MapLibre's, for instance — and Vite
        // injects that link when the chunk loads, so it must not collide with app.css.
        assetFileNames: (asset) =>
          asset.names?.[0] === "main.css" || asset.names?.[0] === "app.css"
            ? "app.css"
            : "[name]-[hash][extname]",
      },
    },
  },
}));
