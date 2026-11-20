#!/usr/bin/env bash
# =================================================================================================
# buildAndRunSite.sh: build the Vue frontend and the .NET backend, combine them, and run the site
# locally exactly as it is packaged for production (Kestrel serving the built SPA from wwwroot),
# together with the Temporal worker and the local dependencies (docker compose).
#
# Usage: ./buildAndRunSite.sh [options]
#   --configuration <Debug|Release>  Build configuration (default: Release if a Six Labors license key is
#                                    available, otherwise Debug; see docs/development.md#imagesharp-license)
#   --skip-frontend                  Reuse the existing app/frontend/dist build
#   --skip-deps                      Don't start docker compose dependencies (they must already be running)
#   --no-worker                      Don't start the Temporal worker
#   -h, --help                       Show this help
#
# Result:  site    http://localhost:5080   (sign in with GitHub; see docs/development.md#github-oauth)
#          worker  http://localhost:8081/healthz
#          Temporal UI http://localhost:8233
# Build output goes to ./out (site/, worker/, migrator/). Ctrl+C stops the site and the worker.
# =================================================================================================
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
frontend="$repo_root/app/frontend"
backend="$repo_root/app/backend"
out="$repo_root/out"

configuration=""
skip_frontend=false
skip_deps=false
run_worker=true

usage() { sed -n '3,19p' "$0" | sed 's/^# \{0,1\}//'; }
info() { printf '\033[1;34m==> %s\033[0m\n' "$*"; }
warn() { printf '\033[1;33mWARN: %s\033[0m\n' "$*" >&2; }
fail() { printf '\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --configuration) configuration="${2:-}"; shift 2 ;;
    --skip-frontend) skip_frontend=true; shift ;;
    --skip-deps) skip_deps=true; shift ;;
    --no-worker) run_worker=false; shift ;;
    -h|--help) usage; exit 0 ;;
    *) fail "Unknown option: $1 (see --help)" ;;
  esac
done

# ---- Prerequisites -----------------------------------------------------------------------------
info "Checking prerequisites"
command -v node >/dev/null || fail "Node.js is not installed (need 24.12+ or 22.18+; see docs/development.md)."
command -v npm >/dev/null || fail "npm is not installed."
command -v dotnet >/dev/null || fail "The .NET SDK is not installed (need 11.0; see docs/development.md)."
dotnet --list-sdks | grep -q '^11\.' || fail ".NET SDK 11 is required (found: $(dotnet --list-sdks | tr '\n' ' '))."
if ! $skip_deps; then
  command -v docker >/dev/null || fail "Docker is required for the local dependencies (or pass --skip-deps)."
  docker info >/dev/null 2>&1 || fail "Docker is not running. Start Docker Desktop (WSL integration enabled) and retry."
fi

# Release builds need the ImageSharp license key (env var or app/backend/sixlabors.lic).
if [[ -z "$configuration" ]]; then
  if [[ -n "${SixLaborsLicenseKey:-}${SIXLABORS_LICENSE_KEY:-}" || -f "$backend/sixlabors.lic" || -f "$repo_root/sixlabors.lic" ]]; then
    configuration=Release
  else
    configuration=Debug
    warn "No Six Labors license key found; building Debug (Release requires it, see docs/development.md#imagesharp-license)."
  fi
fi
[[ "$configuration" == "Debug" || "$configuration" == "Release" ]] || fail "--configuration must be Debug or Release."
export SixLaborsLicenseKey="${SixLaborsLicenseKey:-${SIXLABORS_LICENSE_KEY:-}}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Throwaway development secrets (user-secrets + .env); generated once.
[[ -f "$repo_root/.env" ]] || "$repo_root/scripts/dev-secrets.sh"

api_secrets="$(dotnet user-secrets list --project "$backend/src/PhotoManager.Api" 2>/dev/null || true)"
# Environment variables (Auth__GitHubClientId, ...) override user-secrets, e.g. in CI.
if [[ -z "${Auth__GitHubClientId:-}" ]] && { ! grep -q 'Auth:GitHubClientId' <<<"$api_secrets" || ! grep -q 'Auth:Users:0:GitHubId' <<<"$api_secrets"; }; then
  cat >&2 <<'MSG'
The API needs a GitHub OAuth app and your GitHub user id before it can start:
  1. Create an OAuth app at https://github.com/settings/developers
     Homepage URL http://localhost:5080, callback URL http://localhost:5080/signin-github
  2. GITHUB_CLIENT_ID=... GITHUB_CLIENT_SECRET=... GITHUB_USER_ID=$(curl -s https://api.github.com/users/<login> | jq .id) \
       ./scripts/dev-secrets.sh
See docs/development.md#github-oauth.
MSG
  exit 1
fi

# ---- Build -------------------------------------------------------------------------------------
if ! $skip_frontend; then
  info "Building the frontend (type-check + Vite production build)"
  pushd "$frontend" >/dev/null
  if [[ ! -d node_modules || package-lock.json -nt node_modules/.package-lock.json ]]; then
    npm ci --ignore-scripts --no-audit --no-fund
  fi
  npm run build
  popd >/dev/null
fi
[[ -f "$frontend/dist/index.html" ]] || fail "Frontend build output not found (run without --skip-frontend)."

info "Publishing the backend ($configuration)"
rm -rf "$out"
for project in Api Worker Migrator; do
  target="$(tr '[:upper:]' '[:lower:]' <<<"$project")"
  [[ "$target" == "api" ]] && target=site
  dotnet publish "$backend/src/PhotoManager.$project/PhotoManager.$project.csproj" \
    -c "$configuration" -o "$out/$target" -p:UseAppHost=false -v quiet -nologo
done

# Combine: the API serves the SPA from its wwwroot, exactly like the container image does.
info "Combining frontend and backend (dist -> out/site/wwwroot)"
rm -rf "$out/site/wwwroot"
cp -r "$frontend/dist" "$out/site/wwwroot"

# ---- Dependencies + database -------------------------------------------------------------------
if ! $skip_deps; then
  info "Starting local dependencies (PostgreSQL, Temporal, Azurite, S3 emulator)"
  docker compose -f "$repo_root/docker-compose.yml" up -d --wait postgres temporal azurite s3
fi

info "Applying database migrations"
DOTNET_ENVIRONMENT=Development dotnet "$out/migrator/PhotoManager.Migrator.dll"

# ---- Run ---------------------------------------------------------------------------------------
pids=()
cleanup() {
  info "Stopping"
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM

if $run_worker; then
  info "Starting the Temporal worker (logs: out/worker.log)"
  # exec replaces the subshell, so $! is the dotnet process itself and the trap can stop it.
  (cd "$out/worker" && exec env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:8081 dotnet PhotoManager.Worker.dll >"$out/worker.log" 2>&1) &
  pids+=($!)
fi

info "Starting the site on http://localhost:5080 (Ctrl+C to stop)"
cd "$out/site"
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet PhotoManager.Api.dll &
pids+=($!)
wait "${pids[-1]}"
