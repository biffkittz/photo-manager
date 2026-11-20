# Photo Manager frontend

Vue 3 + TypeScript (strict) + Vite + Tailwind CSS v4 + Pinia + Vue Router + TanStack Vue Query.

See the repository docs:

- [docs/development.md](../../docs/development.md): setup, running locally, tests
- [app/frontend/CLAUDE.md](CLAUDE.md): architecture and conventions for this project

```bash
npm ci
npm run dev          # http://localhost:5080 (proxies /api to the API on :5081)
npm run test:unit    # Vitest (watch mode)
npm run test:e2e     # Playwright + axe-core against the production build
npm run lint:ci && npm run type-check && npm run format:check
```
