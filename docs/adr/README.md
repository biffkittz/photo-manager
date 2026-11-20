# Architecture decision records

Short records of decisions that shape the system, in the order they were made. Each one states the context,
the decision, and the consequences we accepted. Superseded records stay, marked as such.

| # | Decision | Status |
|---|---|---|
| [0001](0001-split-keys.md) | Split keys: only workers can decrypt the archive | Accepted |
| [0002](0002-archive-format-v2.md) | Authenticated v2 format for new files; legacy format read-only | Accepted |
| [0003](0003-private-networking.md) | Private networking for data services | Accepted |
| [0004](0004-temporal-design.md) | Temporal Cloud with encrypted payloads and per-workload task queues | Accepted |
| [0005](0005-container-port.md) | Non-root containers listening on 8080 | Accepted |
| [0006](0006-github-authentication.md) | GitHub OAuth with a numeric-id allowlist and cookie sessions | Accepted |
| [0007](0007-image-processing-library.md) | ImageSharp 4 with the Six Labors community license | Accepted |
| [0008](0008-ef-core-lts.md) | EF Core 10 (LTS) on .NET 11 | Accepted |
| [0009](0009-terraform-state.md) | Terraform state in the existing shared state account, CI scoped to `photo.*` | Accepted |
| [0010](0010-duplicates.md) | Byte-identical photos are hidden, never deleted | Accepted |
| [0011](0011-shared-container-registry.md) | Images in the existing shared container registry | Accepted |
