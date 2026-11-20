# Photo Manager

A private, encrypted family photo library at **photo.biffkittz.com**. It browses the existing encrypted archive
in the Wasabi bucket `photo-bk` (thousands of photos written by `protect.ps1`), lets the family (admins via
allowlisted GitHub accounts, everyone else via Google after an invite from the app) search, view and download
them, and takes bulk uploads that are described by Gemini and then encrypted into the archive. Heavy and sensitive work (cryptography, AI calls, indexing) runs on Temporal workers.

- **Frontend:** Vue 3 + TypeScript, Vite, Tailwind CSS, Pinia, TanStack Vue Query, VeeValidate + Zod
- **Backend:** ASP.NET Core (.NET 11) API serving the SPA; .NET 11 Temporal workers; EF Core + PostgreSQL
- **Platform:** Azure Container Apps (centralus), Key Vault, PostgreSQL flexible server, Blob Storage, private
  networking; Temporal Cloud (`photo.joqqs`); Cloudflare DNS; Terraform; GitHub Actions

## Features

- Gallery with infinite scroll, full-text search (descriptions, filenames, tags), sorting, adjustable density
  and a photo viewer (preview, details, editable description)
- Downloads of originals, single or multi-selected as a ZIP (prepared by workers, streamed by the API)
- Bulk uploads with per-file progress, retry and duplicate detection
- AI descriptions and tags from Gemini, generated before encryption and sealed into the archive file
- Admin actions: re-index the archive, generate missing thumbnails or descriptions, with live progress
- Works with the existing archive as-is; new files use an authenticated encryption format (v2)

## Security at a glance

Only the workers can decrypt the archive (the internet-facing tier holds a separate key for previews), all
secrets live in Key Vault, databases and storage have no public endpoints, Temporal payloads are encrypted
client-side, admins sign in with GitHub OAuth (an allowlist of numeric ids) and invited family members with
Google (bound to the account id when they accept a one-time invite), and sessions are HttpOnly cookies.
Details: [docs/security.md](docs/security.md).

## Quick start (WSL Ubuntu)

```bash
docker compose up -d                 # PostgreSQL, Temporal dev server, Azurite, S3 emulator
./scripts/dev-secrets.sh             # throwaway local keys (see docs/development.md for GitHub OAuth)
./buildAndRunSite.sh                 # builds everything and serves http://localhost:5080
```

Full setup, VS Code configuration, debugging and tests: [docs/development.md](docs/development.md).

## Documentation

| Document | Contents |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Components, data flows, key hierarchy, Temporal design |
| [docs/development.md](docs/development.md) | Prerequisites, VS Code, running, testing, troubleshooting |
| [docs/deployment.md](docs/deployment.md) | Bootstrap, GitHub configuration, Terraform, Key Vault, first release, CI/CD |
| [docs/operations.md](docs/operations.md) | Restarts, key rotation, maintenance jobs, monitoring, disaster recovery |
| [docs/encryption.md](docs/encryption.md) | Legacy and v2 file formats (normative), offline `photo-crypt` tool |
| [docs/security.md](docs/security.md) | Threat model, controls, residual risks, go-live checklist |
| [docs/adr](docs/adr/README.md) | Architecture decision records |
| [infra/README.md](infra/README.md) | Terraform layout, providers, conventions, module documentation |

## Repository layout

```
app/backend     .NET solution: Core, Data, Infrastructure, Api, Worker, Migrator, Cli (+ tests)
app/frontend    Vue 3 SPA (+ Vitest unit tests and Playwright E2E/accessibility tests)
infra           Terraform (bootstrap, modules, environments/prod)
docs            Documentation and ADRs
scripts         dev-secrets.sh (local), bootstrap-keyvault.sh (production secrets)
.github         CI, CodeQL, Terraform and deploy workflows; Dependabot
protect.ps1     The original archive encryption script (format is supported read and write)
```

## Status

v1: feature-complete and tested locally (unit, API, workflow, end-to-end and browser tests; read-only checks
against the real bucket). Not yet deployed: follow [docs/deployment.md](docs/deployment.md).
