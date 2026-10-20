## S-036 — Process files that arrive in the cloud without a manifest
**Epic:** Cloud sources
**Depends on:** S-033, S-006, S-015, S-050
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
- Portability (D-051): read `incoming` and write `originals`, `derivatives` and manifests through `IBlobStore` (S-050), and receive work through `IBlobEventQueue` (S-033, in `Sepiabox.Storage` since S-050); no Azure SDK type outside `Sepiabox.Storage` and the composition root.
- Apply the same "not a video" rules as `process` (D-045): `TrackerModule.IsTrackerModule` before probing, then `VideoProcessor.Probe`, whose `NoVideoStreamException.Reason` says why (no video stream after a second, longer probe in a file that does not look like MPEG, or cover art only). An MPEG-looking file without video is a `VideoProcessingException`, a problem, not left out. A left-out file gets no manifest; log it with the reason instead of failing the message.
- Faces (D-047): the worker looks for them only when the library's face recognition setting from S-022 is on, which is off by default; use `FaceDetector` and `ManifestFaces` like `process --faces`, and write the manifest through `ManifestStore.WriteMergedAsync`. Like `process`, treat faces whose `facesVersion` (missing reads as 1) is older than `ManifestFaces.CurrentVersion` as not processed: detect again and replace them.
- Decide `capturedAt` with `CaptureDate.Resolve` (Sepiabox.Pipeline), never from `MediaMetadata.RawCapturedAt` alone, and set `capturedAtSource` whenever `capturedAt` is set (D-046). The path given to it is the file's path in the cloud source.

### Expected tests
- Integration (Azurite + PostgreSQL): full processing; image build in CI.
