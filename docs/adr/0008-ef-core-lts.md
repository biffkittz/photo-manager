# 0008. EF Core 10 (LTS) on .NET 11

- Status: Accepted (2026-11-19)

## Context

The app targets .NET 11 (as requested). At the time of writing, the Npgsql EF Core provider for EF Core 11 is
only available as a release candidate, while EF Core 10 and Npgsql 10 are LTS releases that run on .NET 11.

## Decision

Use **EF Core 10.0.x + Npgsql.EntityFrameworkCore.PostgreSQL 10.0.x** (central package versions in
`app/backend/Directory.Packages.props`), and the matching `dotnet-ef` local tool.

## Consequences

- Production-supported data access today; upgrade to EF Core 11 when the Npgsql provider is stable
  (Dependabot will propose it; review migrations and breaking changes then).
