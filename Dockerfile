# syntax=docker/dockerfile:1.12
#
# One Dockerfile, three images (pick with --target):
#   web       ASP.NET Core API + the built Vue SPA, served by Kestrel on port 8080
#   worker    Temporal worker (health endpoints on 8080, no public ingress)
#   migrator  one-shot EF Core migrator, run as an Azure Container Apps Job
#
# Runtime images are Ubuntu "chiseled" (distroless: no shell, no package manager) and run as the
# unprivileged `app` user (UID 1654). Kestrel listens on 8080 because Container Apps does not let
# non-root processes bind ports below 1024 (see docs/adr/0005-container-port.md); the public site is
# still served on HTTPS 443 by the Container Apps ingress.
#
# ImageSharp validates a Six Labors license in Release builds. Pass it as a BuildKit secret so it never ends
# up in an image layer or the build history (either form works; the secret is mounted as sixlabors.lic):
#   docker build --target web --secret id=sixlabors_license_key,src=sixlabors.lic -t photo-web .
#   docker build --target web --secret id=sixlabors_license_key,env=SIXLABORS_LICENSE_KEY -t photo-web .
# Without a license, build with --build-arg BUILD_CONFIGURATION=Debug (local experiments only).
#
# Base images are pinned by digest for reproducible, tamper-evident builds; Dependabot bumps them.

ARG NODE_IMAGE=node:26-alpine@sha256:6bd9ab43c0ec0190ca7b2d193906cb8b7edf6e7f4afa43ddeccd6452710bffb5
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:11.0-resolute@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled@sha256:fc0ee12f9ea79bde6cda88c7a34e5467fe383dbb62276be0ec13e5e9cc1654ac

# ---------------------------------------------------------------------------------------------------
# Frontend: Vite production build
# ---------------------------------------------------------------------------------------------------
FROM ${NODE_IMAGE} AS frontend
WORKDIR /src/app/frontend
COPY app/frontend/package.json app/frontend/package-lock.json ./
# --ignore-scripts: dependency install scripts are a common supply-chain attack vector and none are needed.
RUN npm ci --ignore-scripts --no-audit --no-fund
COPY app/frontend/ ./
RUN npm run build-only

# ---------------------------------------------------------------------------------------------------
# Backend: restore (cached layer), then publish the three hosts
# ---------------------------------------------------------------------------------------------------
FROM ${SDK_IMAGE} AS backend
ARG BUILD_CONFIGURATION=Release
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src/app/backend

# The repository-root .editorconfig carries the analyzer configuration; without it the container build would
# apply different code-analysis rules than local and CI builds.
COPY .editorconfig /src/.editorconfig
# Copy only project metadata first so source edits don't invalidate the NuGet restore layer.
COPY app/backend/global.json app/backend/Directory.Build.props app/backend/Directory.Packages.props ./
COPY app/backend/src/PhotoManager.Core/PhotoManager.Core.csproj app/backend/src/PhotoManager.Core/packages.lock.json src/PhotoManager.Core/
COPY app/backend/src/PhotoManager.Data/PhotoManager.Data.csproj app/backend/src/PhotoManager.Data/packages.lock.json src/PhotoManager.Data/
COPY app/backend/src/PhotoManager.Infrastructure/PhotoManager.Infrastructure.csproj app/backend/src/PhotoManager.Infrastructure/packages.lock.json src/PhotoManager.Infrastructure/
COPY app/backend/src/PhotoManager.Api/PhotoManager.Api.csproj app/backend/src/PhotoManager.Api/packages.lock.json src/PhotoManager.Api/
COPY app/backend/src/PhotoManager.Worker/PhotoManager.Worker.csproj app/backend/src/PhotoManager.Worker/packages.lock.json src/PhotoManager.Worker/
COPY app/backend/src/PhotoManager.Migrator/PhotoManager.Migrator.csproj app/backend/src/PhotoManager.Migrator/packages.lock.json src/PhotoManager.Migrator/
# --locked-mode fails the build if a package resolves differently from the committed lock files.
RUN dotnet restore src/PhotoManager.Api/PhotoManager.Api.csproj --locked-mode \
 && dotnet restore src/PhotoManager.Worker/PhotoManager.Worker.csproj --locked-mode \
 && dotnet restore src/PhotoManager.Migrator/PhotoManager.Migrator.csproj --locked-mode

COPY app/backend/src/ src/
RUN --mount=type=secret,id=sixlabors_license_key,target=/src/app/backend/sixlabors.lic,required=false \
    dotnet publish src/PhotoManager.Api/PhotoManager.Api.csproj -c "$BUILD_CONFIGURATION" --no-restore -o /out/web -p:UseAppHost=false \
 && dotnet publish src/PhotoManager.Worker/PhotoManager.Worker.csproj -c "$BUILD_CONFIGURATION" --no-restore -o /out/worker -p:UseAppHost=false \
 && dotnet publish src/PhotoManager.Migrator/PhotoManager.Migrator.csproj -c "$BUILD_CONFIGURATION" --no-restore -o /out/migrator -p:UseAppHost=false

# ---------------------------------------------------------------------------------------------------
# Runtime images
# ---------------------------------------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS runtime-base
# DOTNET_EnableDiagnostics=0 turns off the diagnostic IPC endpoints (debugger/profiler/dump attach) that an
# attacker inside the container could otherwise use; logs, traces and metrics still flow via OpenTelemetry.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_gcServer=0
WORKDIR /app
USER $APP_UID
EXPOSE 8080

FROM runtime-base AS web
COPY --from=backend --chown=root:root /out/web ./
COPY --from=frontend --chown=root:root /src/app/frontend/dist ./wwwroot
ENTRYPOINT ["dotnet", "PhotoManager.Api.dll"]

FROM runtime-base AS worker
COPY --from=backend --chown=root:root /out/worker ./
ENTRYPOINT ["dotnet", "PhotoManager.Worker.dll"]

FROM runtime-base AS migrator
COPY --from=backend --chown=root:root /out/migrator ./
ENTRYPOINT ["dotnet", "PhotoManager.Migrator.dll"]
