# CLAUDE.md: photo-manager

Private, encrypted family photo manager: Vue 3 SPA + ASP.NET Core (.NET 11) API, Temporal (Cloud namespace
`photo.joqqs`) workers, Wasabi archive bucket `photo-bk` (us-east-2), Azure Container Apps (centralus),
PostgreSQL, Key Vault, Terraform. Site: `photo.biffkittz.com`.

## Non-negotiable rules
- **Never read, copy, print, commit or send `scratch`** (gitignored; live credentials). Exclude it from any
  external review. Real-credential use (Wasabi, Gemini, Azure, Cloudflare, Temporal Cloud) needs explicit approval.
- **Consult the user before major architectural decisions.** Security is the top priority.
- **Split keys** (docs/adr/0001): only workers hold the archive password (`Crypto:ArchivePassword`). The API
  refuses to start if it is configured. The API holds the web key (renditions, staging, downloads) only.
- Archive objects: legacy `yyyy-MM-dd/<name>.encrypted` (protect.ps1: salt32|iv16|AES-256-CBC, PBKDF2-SHA1 10k)
  is read-only; new uploads use v2 (`v2/yyyy/MM/<id>.pmv2`, chunked AES-256-GCM, see `PhotoCipherV2`).
- Only identifiers cross Temporal (2 MB payload limit); all payloads are AES-GCM encrypted by `AesGcmPayloadCodec`.
- Workflow code must stay deterministic: no I/O, no `ConfigureAwait(false)`, use `Workflow.UtcNow/NewGuid`.
  Activity calls are expression trees (no `?.` or `[]` inside the lambda).

## Layout
- `app/backend` (.NET 11 SDK, `global.json`; EF Core 10 LTS + Npgsql 10 on purpose): `src/PhotoManager.{Core,Data,
  Infrastructure,Api,Worker,Migrator,Cli}`, `tests/PhotoManager.{Core,Worker,Api,Integration}Tests` (xUnit v3 on
  Microsoft.Testing.Platform). Central package versions in `Directory.Packages.props`; analyzers are errors.
- `app/frontend`: Vue 3 + TS strict + Vite 8 + Tailwind 4 + Pinia + Vue Query + VeeValidate/Zod (own adapter in
  `src/utils/zodSchema.ts`). Tests: Vitest (`src/**/__tests__`), Playwright + axe-core (`e2e/`, runs against the
  production build with the production CSP).
- `Dockerfile` (targets web/worker/migrator, chiseled non-root, port 8080), `docker-compose.yml` (local deps;
  Postgres on host port **55432**), `buildAndRunSite.sh`, `scripts/dev-secrets.sh`.

## Commands (use `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"` if the system SDK is older)
- Backend: `cd app/backend && dotnet build PhotoManager.slnx --no-incremental` and
  `dotnet test --project tests/<Project>`. Integration: `docker compose up -d && ./scripts/dev-secrets.sh &&
  PHOTOMANAGER_INTEGRATION=1 dotnet test --project tests/PhotoManager.IntegrationTests`.
- Migrations: `dotnet ef migrations add <Name> --project src/PhotoManager.Data --startup-project src/PhotoManager.Data`.
- Frontend: `cd app/frontend && npm run lint:ci && npm run type-check && npm run test:unit:ci && npm run test:e2e`.
- Whole site locally: `./buildAndRunSite.sh` (http://localhost:5080; needs a GitHub OAuth app, see script).
- Release builds need the free Six Labors (ImageSharp 4) license: `sixlabors.lic` at the repo root (present,
  gitignored; never read or print it) or env `SixLaborsLicenseKey`; Docker: `--secret id=sixlabors_license_key,src=sixlabors.lic`.
- Never use `pkill -f <pattern>` from the agent shell (it matches its own command line); kill by PID.

## Status (2026-11-20)
v1 plus the fixes of the 2026-11-19 and 2026-11-20 reviews is deployed at photo.biffkittz.com (2026-11-20).
Not committed yet: family members invited from the People page who sign in with Google (docs/adr/0012; members
table with negative user ids; admins stay GitHub-only in Terraform); the Google OAuth client is not created yet
(docs/operations.md, "Inviting family members"). Tests: backend 179 (Core 73, Worker 54, Api 46 running as the
least-privileged web database role, Integration 6), frontend lint/format/types + 50 unit + 18 E2E (3 on a phone-sized touch screen). Terraform state uses the owner's existing account
(`rg-terraform-state`/`demonstration`/`terraform`; CI limited to `photo.terraform.tfstate` by custom roles + ABAC;
docs/adr/0009). Images go to the owner's existing registry `crloginappdemo7fyguj` (`rg-loginapp-demo`, shared with
the login app; repositories `photo-*`; the app identities and their *AcrPull* live in `infra/bootstrap`;
docs/adr/0011). Duplicates are hidden, never deleted (docs/adr/0010). Workers cap downloads at 2,000 photos a day
(docs/adr/0001).

The user chose to skip a scan of the real archive for duplicates (2026-11-19): in-app hiding during the thumbnail
backfill covers it. A weekly ACR task deletes old `photo-*` images (infra/bootstrap). Still to check on the first
deploy: the state ABAC condition and the custom state roles defined in `rg-terraform-state`, that `plan-main` and
`apply` produce the same plan fingerprint, Cloudflare TXT quoting, Temporal namespace API-key auth.

Per-area rules: `app/backend/CLAUDE.md`, `app/frontend/CLAUDE.md`, `infra/CLAUDE.md`. Docs: `docs/` (architecture,
development, deployment, operations, encryption, security, ADRs). CI/CD: `.github/workflows`.
