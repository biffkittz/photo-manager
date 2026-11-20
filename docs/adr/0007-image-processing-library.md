# 0007. ImageSharp 4 with the Six Labors community license

- Status: Accepted (2026-11-19, chosen by the project owner)

## Context

Workers decode untrusted uploads and decrypted archive photos to create WebP renditions, read EXIF dates and
downscale images for Gemini. Options: ImageSharp 3.1 (Apache 2.0, but end of support with open CVEs),
ImageSharp 4 (Six Labors Split License: free community license key required), SkiaSharp/libvips (native
code: larger attack surface and native dependencies in chiseled images).

## Decision

Use **ImageSharp 4** (fully managed decoders) with the free community license. Release builds require the
license (`sixlabors.lic` locally, a BuildKit secret in Docker, the `SIXLABORS_LICENSE` environment secret in
CI); Debug builds only warn. A pixel-count limit is enforced via `Identify` before any decode.

## Consequences

- No native image codecs in the containers; patched, supported library.
- The license must be renewed/kept per Six Labors terms; HEIC is not supported (iPhone uploads must be
  JPEG; the upload page says so).
