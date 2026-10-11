import { fileURLToPath, URL } from "node:url";
import { mergeConfig } from "vite";
import { defineConfig } from "vitest/config";
import viteConfig from "./vite.config";

export default mergeConfig(
  viteConfig({ mode: "test", command: "serve" }),
  defineConfig({
    test: {
      environment: "jsdom",
      environmentOptions: { jsdom: { url: "http://localhost:5173/" } },
      setupFiles: ["src/test/setup.ts"],
      include: ["src/**/*.test.{ts,tsx}"],
      env: { VITE_OIDC_AUTHORITY: "http://idp.test/realms/starterapp" },
      css: false,
    },
    resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  }),
);
