## S-036 — Process files that arrive in the cloud without a manifest
**Epic:** Cloud sources
**Depends on:** S-033, S-006, S-015
**Priority:** Medium
**Size:** M

### Context
Files from cloud sources (Drive, OneDrive) arrive without the PC processing. The worker runs the same pipeline (`Sepiabox.Pipeline`).

### Scope
- `incoming` container (blob + metadata: source type, name, path, external id, reference or custody mode).
- Second Event Grid subscription filtered to `incoming` → `incoming` queue (`maxDeliveryCount` 5).
- `sepiabox-worker process --from-queue`: hash, metadata, thumbnails, video, CLIP (and faces if enabled); writes derivatives and manifest like the PC does; in custody mode moves the original to `originals` (Cold); in reference mode does not store the original; deletes the blob from `incoming`.
- Worker Docker image with FFmpeg and models (downloaded with SHA-256 verification at build time).

### Out of scope
- Connecting accounts (S-037, S-039).

### Acceptance criteria
- [ ] Given a blob in `incoming` (Azurite), when the worker processes it, then the manifest and derivatives exist and the blob is gone from `incoming`.
- [ ] Given reference mode, then nothing is written to `originals`.
- [ ] Given the same file twice, then the result is idempotent.
- [ ] Given the worker Dockerfile, then the CI build produces an image with `ffmpeg`.

### Technical notes
- Reuse the stages from S-004/S-005/S-006/S-015.

### Expected tests
- Integration (Azurite + PostgreSQL): full processing; image build in CI.
