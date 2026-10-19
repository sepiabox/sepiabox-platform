## S-004 — Describe each photo and video in a manifest
**Epic:** Local ingest
**Depends on:** S-002
**Priority:** High
**Size:** M

### Context
The PC processes the drive and the cloud only imports the result (D-005). The result is one JSON manifest per asset, identified by the SHA-256 of its content (D-014). This story creates the `process` command with basic metadata and resume; thumbnails, video, embeddings and faces add fields in later stories.

### Scope
- `sepiabox-ingest process <folder> --out <workdir> [--source-name <name>]` (source name defaults to the folder name).
- Per photo/video: SHA-256 (lowercase hex), size, content type, `capturedAt`, GPS, camera (make/model), width/height.
- Writes `<workdir>/manifests/<sha256>.json` and the JSON Schema `docs/schemas/manifest.v1.json`.
- State in `<workdir>/state.db` (SQLite: absolute path, size, mtime, sha256, status); unchanged files are not re-read.
- Identical files at different paths → one manifest with several `sources`.
- Format v1:
```json
{
  "schemaVersion": 1,
  "id": "<sha256>",
  "kind": "photo",
  "sizeBytes": 2048,
  "contentType": "image/jpeg",
  "capturedAt": "2019-07-14T16:30:05",
  "location": { "latitude": 41.18, "longitude": -8.69 },
  "camera": { "make": "Apple", "model": "iPhone 8" },
  "width": 4032,
  "height": 3024,
  "durationSeconds": null,
  "sources": [ { "type": "local", "name": "HD da Ana", "path": "2019/praia.jpg", "modifiedAt": "2019-07-15T10:00:00Z" } ],
  "derivatives": [],
  "embeddings": {},
  "facesProcessed": false,
  "faces": []
}
```

### Out of scope
- Thumbnails (S-005), video (S-006), upload (S-007).

### Acceptance criteria
- [ ] Given a photo with date, GPS and camera in EXIF, when I run `process`, then the manifest has the correct `capturedAt`, `location` and `camera`.
- [ ] Given two identical files in different folders, when I run `process`, then there is one manifest with 2 `sources`.
- [ ] Given a completed `process`, when I run it again with no changes, then no file is re-read (skipped counter = total).
- [ ] Given a file whose size or mtime changed, when I run it again, then it is reprocessed.
- [ ] Given any generated manifest, when it is validated against `docs/schemas/manifest.v1.json`, then it is valid.
- [ ] Given read-only source files, when I run `process`, then it finishes with no errors (never opens them for writing).

### Technical notes
- `Sepiabox.Pipeline/Media/MetadataReader.cs` (extends the use of MetadataExtractor: `GpsDirectory.GetGeoLocation()`, `ExifIfd0Directory` make/model).
- `Sepiabox.Pipeline/Manifests/Manifest.cs` (records + `System.Text.Json`, camelCase).
- `Sepiabox.Ingest/Processing/ProcessCommand.cs`, `StateStore.cs` (Microsoft.Data.Sqlite).
- GPS fixture: create it in test code with Magick.NET (`ExifProfile.SetValue`) or store a small generated JPEG in `tests/.../TestData`.
- Schema validation in tests: `JsonSchema.Net` package.
- Decisions: D-005, D-014.

### Expected tests
- Unit: metadata reading, manifest serialization, SQLite state.
- Integration: `process` on a temporary folder, resume and reprocessing.
