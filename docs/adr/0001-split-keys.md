# 0001. Split keys: only workers can decrypt the archive

- Status: Accepted (2026-11-19, chosen by the project owner)

## Context

The archive (thousands of photos in Wasabi, encrypted by `protect.ps1` with one password) is the crown jewel.
The web tier is the only internet-facing component and therefore the most likely to be compromised
(dependency vulnerability, deserialization bug, misconfiguration). If the web tier held the archive password,
any web compromise would expose the entire archive, including history.

Options considered: (a) one password everywhere; (b) **split keys**; (c) Key Vault Managed HSM unwrap-only
keys (costly, and the legacy format needs the raw password for PBKDF2 anyway).

## Decision

- Only the Temporal workers (no ingress) receive `Crypto:ArchivePassword`, from their own Key Vault.
- The web tier gets a separate random 256-bit **web key** that encrypts what it must read: renditions
  (thumbnails/previews), staged uploads and prepared downloads (v2 format, key type 2).
- The API refuses to start if `Crypto:ArchivePassword` is configured (`PhotoManager.Api/Program.cs`).
- Full-resolution downloads are prepared by a worker (archive key → web key) into private Blob Storage with
  a one-day lifecycle, then streamed by the API.
- Wasabi credentials exist only on the workers.

## Consequences

- A web compromise exposes previews and, for at most a day, prepared downloads and staged uploads, but never
  the archive or the ability to delete it.
- Downloads of originals are asynchronous (seconds): the SPA polls for readiness.
- Two Key Vaults and two identities; shared keys (web key, payload key) are copied to both by
  `scripts/bootstrap-keyvault.sh`.
