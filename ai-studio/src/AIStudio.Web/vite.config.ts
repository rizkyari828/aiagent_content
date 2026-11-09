import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    port: 5173,
    proxy: {
      // Same-origin dev proxy: the browser talks to Vite, Vite forwards
      // /api/... unchanged to the API. No backend CORS policy is needed.
      "/api": {
        target: "http://127.0.0.1:5002",
        changeOrigin: false,
      },
    },
  },
});
