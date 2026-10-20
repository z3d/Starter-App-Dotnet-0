import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import { tanstackRouter } from '@tanstack/router-plugin/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Aspire (dev/StarterApp.AppHost) launches this app with PORT set and passes the API and identity
// provider addresses as VITE_* variables; `npm run dev` outside Aspire falls back to the defaults
// below, which match the AppHost's dev Keycloak realm and a locally run API.
export default defineConfig(({ mode }) => ({
  plugins: [tanstackRouter({ target: 'react', autoCodeSplitting: true }), react(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: !!process.env.PORT,
    // Same-origin `/api` in the browser; the dev server forwards to the API so the SPA never has
    // to reason about CORS or mixed-content locally. Production serves the SPA behind the same
    // ingress as the API, so the relative path holds there too.
    proxy: {
      '/api': {
        target: process.env.VITE_API_URL ?? 'https://localhost:7286',
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    sourcemap: mode !== 'production',
    target: 'es2023',
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
}))
