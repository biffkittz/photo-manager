# 0004. Temporal Cloud with encrypted payloads and per-workload task queues

- Status: Accepted (2026-11-19)

## Context

Uploads, AI descriptions, re-encryption, archive indexing and downloads are slow, failure-prone and must not
be lost. The project owner chose Temporal (Cloud namespace `photo.joqqs`) and .NET workers.

## Decision

- **Pass references, not data**: workflow inputs/outputs are ids and object keys; photo bytes never enter
  Temporal (2 MB payload limit, privacy).
- **Client-side encryption**: `AesGcmPayloadCodec` encrypts every payload and failure message with the payload
  key (AES-256-GCM, key id in metadata, previous key accepted for rotation), and rejects payloads it did not
  encrypt, so nobody with namespace access but without the key can inject inputs. (Consequence: workflows
  cannot be started with plain JSON from the Temporal UI.)
- **Task queues per workload**: `photo-ingest` (uploads), `photo-interactive` (renditions/downloads a user
  waits for), `photo-batch` (maintenance), `photo-ai` (Gemini, with a server-side activities-per-second limit).
- **Deterministic workflow ids** derived from domain ids (`photo-ingest-{id}`, `renditions-{id}`), so retries
  and concurrent viewers share one execution.
- Long-running maintenance uses **continue-as-new**; the nightly re-index is a Temporal **Schedule**.
- Authentication with **API keys of namespace-scoped service accounts**, one per tier, created outside
  Terraform and stored in Key Vault.
- SDK metrics and tracing are exported to Application Insights via OpenTelemetry.

## Consequences

- Temporal Cloud stores ciphertext only; the Temporal UI shows encrypted payloads (decoding needs a codec
  server, which we deliberately do not run).
- Workflow code must stay deterministic and replay-compatible; changes use `Workflow.Patched`.
- Worker Versioning is deferred while workflows are short-lived.
