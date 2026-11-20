# Development guide (WSL Ubuntu + VS Code)

Everything below runs inside **WSL 2 (Ubuntu)**. Clone the repository into the Linux filesystem
(`~/src/photo-manager`, not `/mnt/c/...`): file watching and builds are much faster there.

## 1. Prerequisites

| Tool | Version | Install (Ubuntu in WSL) |
|---|---|---|
| Docker Desktop | current | Install on Windows, then *Settings > Resources > WSL integration* > enable your distro. Verify: `docker info` |
| .NET SDK | **11.0** (see `app/backend/global.json`) | `sudo apt-get install -y dotnet-sdk-11.0` (Ubuntu 26.04 archive or the Microsoft package feed). Alternative without sudo: `curl -fsSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 11.0` then add `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"` to `~/.bashrc` |
| Node.js | 26 (24.12+ works) | `curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/master/install.sh \| bash`, then `nvm install 26` |
| Terraform | 1.17+ | [HashiCorp apt repository](https://developer.hashicorp.com/terraform/install) |
| Azure CLI | current | `curl -sL https://aka.ms/InstallAzureCLIDeb \| sudo bash` (deployment only) |
| Temporal CLI | 1.10+ (optional) | `curl -sSf https://temporal.download/cli.sh \| sh` (inspect workflows from the terminal) |
| jq, git | any | `sudo apt-get install -y jq git` |

Check: `dotnet --list-sdks` shows `11.0.x`, `node -v` shows 24.12+ or 26.x, `docker compose version` works.

## 2. VS Code

1. Install VS Code on Windows and the **WSL** extension. In the Ubuntu terminal: `cd ~/src/photo-manager && code .`
   (or *File > Open Workspace from File...* > `photo-manager.code-workspace` for the multi-root view).
2. Accept the recommended extensions prompt (listed in `.vscode/extensions.json`):
   C# Dev Kit, Vue (Official), ESLint, Prettier, Oxc, Tailwind CSS IntelliSense, Vitest, Playwright,
   HashiCorp Terraform, Container Tools, Azure Container Apps, GitHub Actions, PostgreSQL, EditorConfig, YAML,
   Markdown Mermaid, markdownlint, Code Spell Checker, PowerShell.
3. If the C# extension picks up an older system SDK, point it at .NET 11 (put `~/.dotnet` first on `PATH`
   in `~/.bashrc` and restart the WSL window).

Debug configurations (Run and Debug view): **API**, **Worker**, **Migrator**, **Frontend (Vite)**, **Browser**,
and the compound **Full stack (deps + API + worker + Vite)**. Tasks (*Terminal > Run Task*) cover dependencies,
builds, tests, migrations, Terraform validation and `buildAndRunSite.sh`.

## 3. First-time setup

```bash
cd ~/src/photo-manager
docker compose up -d --wait   # PostgreSQL 17 (localhost:55432), Temporal (7233, UI http://localhost:8233),
                              # Azurite (10000), S3 emulator standing in for Wasabi (9090, bucket photo-bk-dev)
./scripts/dev-secrets.sh      # throwaway dev keys -> dotnet user-secrets + .env (never real secrets)
(cd app/frontend && npm ci && npx playwright install chromium)
(cd app/backend && dotnet restore PhotoManager.slnx && dotnet tool restore)
```

PostgreSQL is published on **55432** because many WSL setups already run a PostgreSQL on 5432, and runs the
same major version as production (17). `app/frontend/.npmrc` disables dependency install scripts for every
`npm` command (supply-chain hardening; nothing needs them), which is why Playwright's browser is installed
explicitly.

### GitHub OAuth

Needed to sign in locally.


1. GitHub > *Settings > Developer settings > OAuth Apps > New OAuth App*:
   Homepage `http://localhost:5080`, Authorization callback `http://localhost:5080/signin-github`.
2. Generate a client secret, then store everything with your numeric GitHub id:

```bash
GITHUB_CLIENT_ID=... GITHUB_CLIENT_SECRET=... \
GITHUB_USER_ID=$(curl -s https://api.github.com/users/<your-login> | jq .id) \
./scripts/dev-secrets.sh
```

Every local mode (Vite dev server, `buildAndRunSite.sh`, the compose `web` container) serves on
`http://localhost:5080`, so this one OAuth app works for all of them. Use a **separate** OAuth app for
production.

### ImageSharp license

ImageSharp 4 (thumbnails, previews, Gemini input) checks a Six Labors license at build time. **Debug builds
only warn; Release builds fail without it.** The community license is free for this project's use under the
Six Labors Split License. Put the license file at the repository root as `sixlabors.lic` (gitignored) or set
`SixLaborsLicenseKey`. Docker builds receive it as a BuildKit secret
(`--secret id=sixlabors_license_key,src=sixlabors.lic`). In GitHub, only the Deploy workflow needs it, as the
`SIXLABORS_LICENSE` secret of the **production environment**; CI builds Debug and needs no license.

## 4. Running

| Goal | Command |
|---|---|
| Day-to-day development (hot reload) | VS Code compound **Full stack**, or: `docker compose up -d`, then `dotnet run --project app/backend/src/PhotoManager.Api` (:5081), `dotnet run --project app/backend/src/PhotoManager.Worker`, `cd app/frontend && npm run dev` (:5080, proxies `/api`) |
| Production-style package (SPA served by Kestrel) | `./buildAndRunSite.sh` -> http://localhost:5080 (see `--help`) |
| The actual container images | `docker compose --profile app up -d --build` (web on :5080) |
| Database migrations | `DOTNET_ENVIRONMENT=Development dotnet run --project app/backend/src/PhotoManager.Migrator` (the API does not migrate on startup) |

Local development never touches the real archive: the worker points at the S3 emulator, and `Gemini:UseFake`
returns placeholder descriptions (it is rejected outside Development). To try real Gemini locally:
`dotnet user-secrets set Gemini:ApiKey <key> --project app/backend/src/PhotoManager.Worker` and set
`Gemini:UseFake=false`.

Useful URLs: Temporal UI http://localhost:8233 (payloads appear encrypted: that is the codec working),
worker health http://localhost:8081/healthz.

## 5. Tests

| Suite | Command | Notes |
|---|---|---|
| Core (crypto, protect.ps1 compatibility, domain) | `cd app/backend && dotnet test --project tests/PhotoManager.Core.Tests` | Fixtures in `TestData/Legacy` were produced by the real `protect.ps1` code (`generate-vectors.ps1`) |
| Worker (workflows, codec, sanitizers) | `dotnet test --project tests/PhotoManager.Worker.Tests` | Temporal time-skipping test server (downloaded on first run) |
| API (auth, CSRF, headers, uploads, search) | `dotnet test --project tests/PhotoManager.Api.Tests` | Needs Docker (Testcontainers PostgreSQL) |
| End to end | `PHOTOMANAGER_INTEGRATION=1 dotnet test --project tests/PhotoManager.IntegrationTests` | Needs `docker compose up -d` and `.env`; add `PHOTOMANAGER_INTEGRATION_EXTERNAL_WORKER=1` to exercise the worker container |
| Frontend unit | `cd app/frontend && npm run test:unit` | Vitest (Jest-compatible API) + Vue Test Utils |
| Frontend E2E + accessibility | `npm run test:e2e` | Playwright against the production build served with the production CSP; axe-core WCAG 2.1 AA checks. First run: `npx playwright install chromium` |
| Lint/format/types | `npm run lint:ci && npm run format:check && npm run type-check` | Backend analyzers run as part of every build (warnings are errors) |

## 6. Common tasks

- **Add a migration:** change entities in `app/backend/src/PhotoManager.Data`, then
  `dotnet ef migrations add <Name> --project src/PhotoManager.Data --startup-project src/PhotoManager.Data`
  (from `app/backend`). Review the generated SQL-affecting code; new tables need grants in
  `PhotoManager.Migrator/RoleProvisioner.cs`.
- **Add an activity:** see `app/backend/CLAUDE.md` (naming, idempotency, error translation, profiles).
- **Decrypt an archive file offline:** `dotnet run --project app/backend/src/PhotoManager.Cli -- decrypt in out`
  (password from `PHOTO_CRYPT_PASSWORD` or prompted). See [encryption.md](encryption.md#offline-tool).

## 7. Troubleshooting

| Symptom | Fix |
|---|---|
| `password authentication failed for user "photomanager"` | You are hitting a host PostgreSQL. The compose database is on **55432** |
| API exits with `Auth:Users must list at least one numeric GitHub id` | Run the GitHub OAuth step above |
| API exits with `The web tier must not be configured with Crypto:ArchivePassword` | Remove that user-secret from the **API** project; only the worker may have it |
| `NETSDK` / analyzer errors only in Docker | The build uses the repository `.editorconfig`; run `dotnet build` locally to see the same diagnostics |
| `Release builds require a valid license` | See [ImageSharp license](#imagesharp-license) |
| Testcontainers cannot start PostgreSQL | Docker Desktop WSL integration must be enabled for your distro |
| A `photo-manager_pgdata` volume is left over | It holds a database from before the move to PostgreSQL 17 (the compose file now uses `pgdata17`). Remove it when you no longer need that local data: `docker volume rm photo-manager_pgdata` |
| Thumbnails stay "Preparing preview..." locally | Is the worker running? Check http://localhost:8233 for `EnsureRenditions` workflows |
