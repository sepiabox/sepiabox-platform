## S-006 — Generate video posters and 720p versions
**Epic:** Local ingest
**Depends on:** S-004
**Priority:** High
**Size:** M

### Context
Original videos are large and stay in Cold; the web shows a poster and plays a 720p version (D-015). The ingest laptop has an NVIDIA GPU, so the hardware encoder is optional.

### Scope
- Video stage in `process` using external FFmpeg (`ffmpeg`/`ffprobe` on PATH or `--ffmpeg <folder>`).
- Duration via `ffprobe`; `poster.webp` (frame at 10% of the duration, longest side 1280) and `thumb-256.webp`; `proxy-720p.mp4` (H.264 CRF 28, preset veryfast, AAC 96k, `+faststart`, smaller videos not upscaled).
- `--hw nvenc` uses `h264_nvenc`.
- Manifest: `durationSeconds` and derivatives.
- Without FFmpeg: a clear warning, videos marked as pending in the state, photos processed normally.

### Out of scope
- Audio transcription.

### Acceptance criteria
- [ ] Given a 3 s 1920×1080 video (generated with `ffmpeg -f lavfi -i testsrc`), when I run `process`, then `proxy-720p.mp4` is 1280×720 at ~3 s and `poster.webp` exists.
- [ ] Given a 640×360 video, when I run `process`, then the proxy stays 640×360.
- [ ] Given FFmpeg is missing, when I run `process`, then it exits with code 0, a warning, and videos pending in the state.
- [ ] Given a second `process`, when it runs, then videos are not re-encoded.

### Technical notes
- `Sepiabox.Pipeline/Video/VideoProcessor.cs` with `System.Diagnostics.Process`; arguments built as a list (no shell string concatenation).
- Tests that use FFmpeg: `[Trait("Category","Integration")]` (the integration job installs `ffmpeg`).
- Decisions: D-015.

### Expected tests
- Unit: FFmpeg argument construction, scale calculation.
- Integration: real conversion of a generated video.
