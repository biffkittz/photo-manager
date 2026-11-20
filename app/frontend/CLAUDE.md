# CLAUDE.md: app/frontend (Vue 3 + TypeScript)

Read the root `CLAUDE.md` first.

## Commands (from `app/frontend`)

```bash
npm ci --ignore-scripts          # install scripts are disabled on purpose (supply chain)
npm run dev                      # http://localhost:5080, proxies /api, /signin-github and /signin-google to :5081
npm run lint:ci && npm run format:check && npm run type-check && npm run test:unit:ci
npm run test:e2e                 # Playwright + axe against the production build served with the production CSP
npm audit                        # must stay at 0 vulnerabilities (overrides in package.json)
```

## Structure

`src/features/<feature>` (auth incl. the invite page, gallery, uploads, jobs, people, errors) holds views and
feature components; `src/components` shared UI; `src/composables` reusable logic (`useX`); `src/stores` Pinia setup stores;
`src/api` the typed fetch client and Vue Query hooks; `src/utils/zodSchema.ts` the VeeValidate/Zod adapter;
`src/styles` Tailwind 4 theme tokens. Unit tests live in `__tests__` next to the code; E2E in `e2e/`.

## Conventions (project rules)

- `<script setup lang="ts">` only; typed `defineProps<...>()`/`defineEmits<...>()`, `defineModel` for v-model;
  macros in the order props, emits, model. Strict TypeScript, no `any`.
- Server state in TanStack Vue Query (keys in one place, invalidate after mutations); client state in Pinia
  setup stores; forms with VeeValidate + Zod schemas.
- Tailwind utilities and theme tokens only (no hard-coded colors); keep WCAG 2.1 AA contrast (axe runs in E2E).
- **Security:** never `v-html` (lint error); never store tokens in localStorage/sessionStorage (the session is
  an HttpOnly cookie); every state-changing request goes through the API client, which sends the
  `X-CSRF-TOKEN` header; no inline scripts/styles in `index.html` and no `eval`-style code (CSP
  `script-src 'self'`; E2E fails on CSP violations); render model/user text with interpolation only.
- Accessibility: semantic elements, labels for every control, visible focus, keyboard support for dialogs
  and the viewer, `aria-live` for async status.
- Errors: user-facing notifications via the notifications store; no third-party error reporting
  (see `src/plugins/errorHandler.ts` and docs/operations.md#error-reporting).
- Session end (sign-out, 401, a session check answering "signed out", or another tab, via the
  `photo-manager-auth` BroadcastChannel): `auth.onSessionEnded` listeners (registered in `main.ts`) clear the Vue
  Query cache and the upload queue and leave pages that need a session for sign-in. A session check that gets no
  answer concludes nothing (`auth.loaded` stays false; the sign-in page shows `error=unavailable`). Requests
  made outside `apiFetch` must call `notifyUnauthorized()` on 401, and on a CSRF rejection (`isCsrfProblem`) call
  `refreshCsrfToken()` and retry once, as `apiFetch` does.
- Persisted preferences (`persist` store option) are untrusted input: list them in `pick` and add a `validate`.
- `.npmrc` sets `ignore-scripts=true`: dependencies never run install scripts.
