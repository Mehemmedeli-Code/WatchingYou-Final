import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { fileURLToPath, URL } from "node:url";

// The Razor host is the page; Vite only produces the island bundle.
// Fixed output filenames mean _Layout.cshtml can hard-code the script tag instead of
// reading a manifest at request time.
export default defineConfig({
  plugins: [react(), tailwindcss()],
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
    },
  },
  build: {
    outDir: "../src/MovieRental.Host/wwwroot/app",
    emptyOutDir: true,
    manifest: false,
    rollupOptions: {
      input: fileURLToPath(new URL("./src/main.tsx", import.meta.url)),
      output: {
        entryFileNames: "app.js",
        chunkFileNames: "[name].js",
        assetFileNames: "app.[ext]",
      },
    },
  },
});
