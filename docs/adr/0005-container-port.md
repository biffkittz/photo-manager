# 0005. Non-root containers listening on 8080

- Status: Accepted (2026-11-19, chosen by the project owner; supersedes the initial "port 80" requirement)

## Context

The original requirement was Kestrel on port 80. Binding ports below 1024 requires root (or extra
capabilities), while the .NET chiseled images run as a non-root user (`$APP_UID`, 1654) by default.

## Decision

Containers run as the non-root user on **port 8080** (`ASPNETCORE_HTTP_PORTS=8080`). Azure Container Apps
ingress terminates TLS on 443 and forwards to 8080; locally, compose maps `localhost:5080` to 8080.

## Consequences

- No root process in any container; images have no shell or package manager.
- External URLs are unchanged (HTTPS on 443); only the internal target port differs from the original request.
