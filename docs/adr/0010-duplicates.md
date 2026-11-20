# 0010. Byte-identical photos are hidden, never deleted

- Status: Accepted (2026-11-19)

## Context

The archive was written by `protect.ps1`, which skips content it has already uploaded by checking SHA-512
hashes in a local `photo.db`. Copies can still exist (a lost or second `photo.db`, two machines). The library
should show each photo once, and the database should be able to guarantee that, but the archive is the
family's only copy of these photos and must not be pruned by automation.

## Decision

- Photos are compared by the SHA-256 of their decrypted content, computed when a legacy photo is first decoded
  (**Generate missing thumbnails**) and on every upload.
- A byte-identical copy of a photo already in the library becomes `Duplicate` and is hidden; the earliest copy
  stays. A unique partial index (`ux_photos_sha256_ready`) allows only one ready photo per hash, so concurrent
  ingests and backfills cannot race past the check; the losing copy is marked `Duplicate`.
- Archive objects are never deleted by the app. Removing duplicate objects from the bucket is a manual,
  optional step (bucket versioning keeps deletions recoverable).

## Consequences

- No data loss, and `protect.ps1` restores keep working for every key in `photo.db`.
- Near-duplicates (resized or re-saved copies) are not detected; only identical bytes are.
- Hashing requires decrypting each legacy photo once, which the thumbnail backfill does anyway.
