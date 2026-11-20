# 0002. Authenticated v2 format for new files; legacy format read-only

- Status: Accepted (2026-11-19, chosen by the project owner)

## Context

`protect.ps1` writes `salt|iv|AES-256-CBC` with PBKDF2-HMAC-SHA1 (10 000 iterations). It has no integrity
protection, a weak KDF by current standards, and requires the whole file in memory. Existing files must stay
readable forever (by the app and by `protect.ps1` itself).

## Decision

- Keep reading the legacy format (proven byte-compatible both ways by tests generated with the real script).
- Write all new data in **v2**: versioned 80-byte header, PBKDF2-HMAC-SHA256 (600 000 iterations) or a raw
  key, HKDF-SHA256 per-file keys bound to the full header, AES-256-GCM in 64 KiB chunks with the STREAM nonce
  construction (counter + final flag), and optional sealed JSON metadata (filename, description, hashes).
- Object keys `v2/yyyy/MM/<photo-id>.pmv2` reveal nothing about content.
- Ship an offline tool (`photo-crypt`) that reads both formats without the app.

Normative specification: [docs/encryption.md](../encryption.md).

## Consequences

- Tampering, truncation, reordering and header edits are detected; decryption streams with bounded memory.
- The bucket is self-describing: the database can be rebuilt from v2 metadata.
- Two code paths to maintain; legacy files remain unauthenticated until a future re-encryption job.
