import { fileURLToPath, URL } from 'node:url'

import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'
import vueDevTools from 'vite-plugin-vue-devtools'

// The API runs on :5081 during development and Vite serves the SPA on :5080, proxying API and OAuth
// callback paths. One origin (localhost:5080) for every local mode means one GitHub OAuth app
// (callback http://localhost:5080/signin-github), and optionally one Google OAuth client (redirect URI
// http://localhost:5080/signin-google), work for `npm run dev`, buildAndRunSite.sh and compose.
const apiTarget = process.env.API_URL ?? 'http://localhost:5081'
const proxied = { target: apiTarget, changeOrigin: false, xfwd: true }

export default defineConfig({
  plugins: [vue(), tailwindcss(), vueDevTools()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5080,
    strictPort: true,
    proxy: {
      '/api': proxied,
      '/signin-github': proxied,
      '/signin-google': proxied,
      '/healthz': proxied,
    },
  },
  // `vite preview` (used by the Playwright E2E suite) sends the production CSP, so any inline script/style
  // that would break behind the real API (SecurityHeadersMiddleware) fails the tests.
  preview: {
    port: 4173,
    strictPort: true,
    headers: {
      'Content-Security-Policy':
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data: https://avatars.githubusercontent.com; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'",
    },
  },
  build: {
    // No source maps in production bundles: they would publish the original source.
    sourcemap: false,
    target: 'es2023',
    chunkSizeWarningLimit: 300,
  },
})
