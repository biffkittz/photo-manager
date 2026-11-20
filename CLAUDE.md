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

## Status (2026-11-19)
v1 plus all confirmed Gemini-review fixes, verified locally; nothing is deployed or committed yet. Tests: backend
135 (Core 69, Worker 40, Api 23, Integration 3), frontend lint/format/types + 30 unit + 5 E2E. Terraform state uses
the owner's existing account (`rg-terraform-state`/`demonstration`/`terraform`, keys `photo.*.tfstate`, Entra
auth; docs/adr/0009). Duplicates are hidden, never deleted (docs/adr/0010).

Open with the user: (1) the real-archive duplicate scan was blocked by the permission system and needs their
decision (run it, or let the deployed thumbnail backfill find duplicates); (2) container registry clean-up job
(explained in docs/operations.md#container-registry-storage); (3) items to verify on first deploy (state ABAC
condition, Cloudflare TXT quoting, Temporal namespace API-key auth).

Per-area rules: `app/backend/CLAUDE.md`, `app/frontend/CLAUDE.md`, `infra/CLAUDE.md`. Docs: `docs/` (architecture,
development, deployment, operations, encryption, security, ADRs). CI/CD: `.github/workflows`.
