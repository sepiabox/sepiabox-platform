## S-005 — Generate photo thumbnails
**Epic:** Local ingest
**Depends on:** S-004
**Priority:** High
**Size:** S

### Context
The web shows thumbnails and previews, never the originals (which stay in Cold). They are generated on the PC during `process` (D-015).

### Scope
- Thumbnail stage in `process` with Magick.NET (`Magick.NET-Q8-AnyCPU`): `thumb-256.webp` and `preview-1280.webp` (longest side, aspect ratio kept, EXIF orientation applied, metadata stripped, quality 80) in `<workdir>/derivatives/<sha256>/`.
- Records in the manifest's `derivatives`: `{ "name": "thumb-256", "path": "derivatives/<id>/thumb-256.webp", "width": 256, "height": 192, "contentType": "image/webp" }`.
- Images smaller than the target are not upscaled.
- Failure on one image → a problem in the report; processing continues.

### Out of scope
- Video (S-006).

### Acceptance criteria
- [ ] Given a 4000×3000 JPEG, when I run `process`, then `thumb-256.webp` at 256×192 and `preview-1280.webp` at 1280×960 exist.
- [ ] Given a JPEG with `Orientation=6`, when I run `process`, then the thumbnail is portrait (height > width).
- [ ] Given a corrupted `.jpg`, when I run `process`, then the manifest has no derivatives and the problem is reported.
- [ ] Given a second `process` with no changes, when it runs, then thumbnails are not regenerated.
- [ ] Given a photo with GPS, when I run `process`, then the thumbnail contains no EXIF.

### Technical notes
- `Sepiabox.Pipeline/Imaging/ThumbnailGenerator.cs`.
- Fixtures generated in tests with Magick.NET (solid colour, orientation via `ExifProfile`).
- Decisions: D-015.

### Expected tests
- Unit: dimensions, orientation, metadata stripping, corrupted image.
