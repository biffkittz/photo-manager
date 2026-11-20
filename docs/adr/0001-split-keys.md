# 0001. Split keys: only workers can decrypt the archive

- Status: Accepted (2026-11-19, chosen by the project owner). Consequences corrected and hardened 2026-11-20
  after a security review.

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

What the split protects, even if the web tier is fully compromised:

- The archive password and the Wasabi credentials: the archive cannot be copied and decrypted offline, and it
  cannot be deleted or overwritten.
- The whole archive at once: decryption happens only in the workers, one request at a time, under their rules.

What it does **not** protect: downloads are a feature, so a compromised web tier can ask the workers to decrypt
originals, as any signed-in family member can. Bounding that is the workers' job, not the web tier's:

- **Daily cap.** Workers prepare at most `Worker:MaxDownloadPhotosPerDay` photos (default 2,000) in any 24 hours,
  across all users, counted from a column only workers write (`prepared_downloads.prepared_at`). Every prepared
  download is logged by the worker itself.
- **Only real archive photos.** A worker decrypts an archive object only if its key belongs to that photo (a v2
  key carries the photo's id; a legacy key must be a `protect.ps1` file). The web tier's database role cannot
  change where a photo is archived, how it is encrypted, its content hash or its owner (column-level grants in
  `RoleProvisioner`), so it cannot point the workers at other objects in the bucket.
- **Staged uploads stay gone.** The web tier's blob role (`<project>-<environment>-blob-data-writer`, defined in
  `infra/bootstrap`) has no container rights, so it cannot restore staged uploads that ingest deleted (blob soft
  delete keeps them for 7 days; restoring one needs `containers/write`). A compromised web tier still sees every
  upload that arrives while it is compromised, and prepared downloads for up to a day.
- **Staged content is checked.** Everything a worker renders, describes or archives from staging must hash to
  what its inspection saw, so content swapped in staging is never archived or rendered.

Other consequences:

- Downloads of originals are asynchronous (seconds): the SPA polls for readiness.
- Two Key Vaults and two identities; shared keys (web key, payload key) are copied to both by
  `scripts/bootstrap-keyvault.sh`.
