import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  // Native since Vite 7; no vite-tsconfig-paths plugin needed for the "@/" alias.
  resolve: { tsconfigPaths: true },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./test/setup.ts"],
    include: ["{app,components,lib,hooks}/**/*.test.{ts,tsx}"],
  },
});
