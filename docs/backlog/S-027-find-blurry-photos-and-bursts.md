## S-027 — Find blurry photos and bursts
**Epic:** Organization
**Depends on:** S-005, S-013
**Priority:** Low
**Size:** M

### Context
Family drives have many blurry photos and near-identical sequences. Help review them without deleting anything.

### Scope
- In `process`: sharpness (variance of the Laplacian over the grayscale image at 512 px) and a 64-bit dHash; manifest `quality: { "sharpness": 123.4, "dhash": "<hex>" }`; the import stores them.
- Page `/organize`: bursts (photos ≤ 2 s apart with dHash Hamming distance ≤ 10) and blurry photos (sharpness below a configurable threshold).
- "Hide" action (flag `hidden`); hidden assets are excluded from search by default. DSL `quality: { "blurry": true }`, `includeHidden`.

### Out of scope
- Deleting originals.

### Acceptance criteria
- [x] Given an image and its blurred version, when sharpness is computed, then the blurred one has lower sharpness.
- [x] Given an image and the same image resized, when dHashes are compared, then the distance is ≤ 4; for different images it is > 20.
- [x] Given 3 photos 1 s apart with close dHashes, when bursts are detected, then they form a burst.
- [x] Given a hidden asset, when searching, then it does not appear by default and appears with `includeHidden`.

### Technical notes
- `Sepiabox.Pipeline/Imaging/QualityAnalyzer.cs` with Magick.NET.

### Expected tests
- Unit: sharpness, dHash, grouping. Integration: hiding and search.

### Status
- `done` (2026-10-22): implemented (D-098). Sharpness and hash come from the preview, so photos processed before this get them on the next `process` without reading the drive again; the question translator does not produce `quality` or `includeHidden` (D-098, so no live run was needed).
