# CLAUDE.md: app/backend (.NET 11)

Read the root `CLAUDE.md` first (non-negotiable rules: `scratch`, split keys, real-credential approval).

## Commands (from `app/backend`)

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"   # if the system SDK is older than 11
dotnet build PhotoManager.slnx --no-incremental                 # analyzers are errors; --no-incremental avoids stale passes
dotnet test --project tests/PhotoManager.Core.Tests             # also Worker.Tests, Api.Tests (Docker), IntegrationTests
dotnet tool restore && dotnet ef migrations add <Name> --project src/PhotoManager.Data --startup-project src/PhotoManager.Data
dotnet restore PhotoManager.slnx --force-evaluate               # after changing Directory.Packages.props (updates lock files)
```

Integration tests need `docker compose up -d`, `./scripts/dev-secrets.sh` and `PHOTOMANAGER_INTEGRATION=1`.
Release builds need the Six Labors license (`sixlabors.lic` at the repo root or in this folder, gitignored;
never read or print it).

## Projects

| Project | Role | Notes |
|---|---|---|
| `PhotoManager.Core` | Crypto (legacy + v2), domain helpers, workflow contracts | No I/O dependencies; fully unit-tested |
| `PhotoManager.Data` | EF Core 10 model, migrations | Enums stored as strings; snake_case; `xmin` concurrency |
| `PhotoManager.Infrastructure` | Azure/Wasabi/Gemini/Temporal clients, options, imaging, telemetry | Library: `ConfigureAwait(false)` everywhere |
| `PhotoManager.Api` | Web tier: auth, endpoints, SPA hosting | Must never receive `Crypto:ArchivePassword` |
| `PhotoManager.Worker` | Temporal workers (4 task queues), activities, workflows, nightly schedule | Holds the archive password |
| `PhotoManager.Migrator` | Applies migrations and database grants (Container Apps Job) | Update `RoleProvisioner` for new tables, and its web-editable photo columns when the web tier must update a new one (the API tests run as that role) |
| `PhotoManager.Cli` | `photo-crypt` offline tool | Secrets from env vars or hidden prompt, never args |

## Code conventions

- File-scoped namespaces, one public type per file, explicit access modifiers, XML docs on every public API,
  `sealed` by default, nullable enabled, `TreatWarningsAsErrors` with `latest-recommended` analyzers.
- Central package versions in `Directory.Packages.props`; lock files committed; CI restores in locked mode.
- Settings are options classes bound from configuration; required values are validated at startup
  (`Validate(...)` + `ValidateOnStart()`), so misconfiguration fails fast instead of at first use.
- Logging: structured messages, never secrets, keys, tokens, image bytes or full descriptions.
- Tests: xUnit v3 on Microsoft.Testing.Platform (`dotnet test --project ...`), NSubstitute, Testcontainers.

## Temporal rules

- Workflow code is deterministic: no I/O, no `DateTime.UtcNow`/`Guid.NewGuid` (use `Workflow.UtcNow`/
  `Workflow.NewGuid`), no `ConfigureAwait(false)`, no `Task.Run`, no static mutable state.
- Activity calls are expression trees: build inputs outside the lambda (no `?.`, `[]`, or pattern matching inside).
- Only ids/keys cross Temporal (2 MB limit); bytes go through Blob Storage or Wasabi.
- Every activity: an `ActivityProfiles` entry (StartToClose above downstream timeouts, bounded retries),
  idempotent behaviour, and permanent failures translated via `ActivityErrors.Permanent(...)` (non-retryable).
- Changing a workflow's command sequence breaks running executions: use `Workflow.Patched("id")` or make sure
  no executions are open. Keep continue-as-new boundaries for long loops.
- Workflow ids come from `WorkflowIds` (deterministic, domain-derived); task queues from `TaskQueues`.

## Data rules

- At most one `Ready` photo per content hash (`ux_photos_sha256_ready`, see `DbErrors`): code that sets a photo
  Ready must handle that unique violation by marking the photo `Duplicate`. The archive is never pruned
  (docs/adr/0010).
- Ownership (uploads, prepared downloads) and audit actors use the user id (`User.UserId()`), never the name: the
  numeric GitHub id (positive) or the `family_members` id of a Google-invited member (negative), so `!= 0`, never
  `> 0`, means "signed in". Photo endpoints only expose `Ready` photos.
- Admins exist only in configuration (`Auth:Users`, GitHub). Members invited from the People page sign in with
  Google, are recognized only by the Google account id bound when they accept, and are always plain members.
- Anything that records state and then starts a workflow must undo that state if Temporal is unreachable
  (`TemporalUnavailable`), or the row stays "pending" forever. `PhotoWorkflowClient` starts are deliberately not
  cancellable by the request (a closed tab must not abandon a start halfway); they time out instead.
- The web tier's database role may update only `RoleProvisioner.WebEditablePhotoColumns` on `photos`; where a photo
  is archived, its encryption, hash and owner are written by workers only. Workers decrypt an archive object only
  if `ArchiveObjectKeys.BelongsTo` the photo, and ingest activities verify staged content against the inspected
  hash. Job cancels always target a recorded run id, never a bare (reused) workflow id.

## Crypto rules

- Never change the v2 byte format without bumping the version byte and updating `docs/encryption.md` and the
  tests; legacy format stays byte-compatible with `protect.ps1` (fixtures in `tests/**/TestData/Legacy`).
- Zero plaintext buffers after use (`CryptographicOperations.ZeroMemory`); stream, do not buffer whole files.
- Web-tier code may only use the web key (`PhotoKeyRing` web keys); archive decryption belongs in the worker.
- Always pass the expected key type when opening v2 data (`expectedKeyType`), so the type is checked before any
  key is resolved. The Temporal payload codec rejects payloads it did not encrypt.
