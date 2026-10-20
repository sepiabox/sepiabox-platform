## S-061 — Generate a fixture library with known contents
**Epic:** Questions
**Depends on:** S-004, S-053
**Priority:** High
**Size:** S

### Context
The MVP acceptance test (S-054) needs a folder of photos and videos whose counts in unique assets per year × kind, undated, folder-dated and per city are known in advance, generated in the test rather than committed (CLAUDE.md: generate larger media in the test).

### Scope
- `FixtureLibrary` in `tests/Shared`: writes a folder with photos (EXIF dates, some with GPS in Lisbon and Porto), phone-style videos with QuickTime creation dates and ISO 6709 locations (S-053, through FFmpeg), files dated only by a `YYYY-MM-DD` parent folder (D-046), **truly undated** files (no metadata date, no dated file name, no dated parent folder), files without GPS, an exact duplicate, and a non-media file; it returns the expected numbers in unique assets (per year × kind, folder-dated, truly undated, per city, duplicates) computed from what it wrote.
- Deterministic: a seed gives the same files and numbers.

### Out of scope
- Running the pipeline over it (S-054).

### Acceptance criteria
- [x] Given a seed, when the library is generated twice, then the files and the expected numbers are identical.
- [x] Given a generated library, when `process` runs on it, then the manifests in the work folder, counted per year × kind and undated in the test, equal the generator's expected numbers, with the duplicate counted once and the folder-dated files in their year. (S-054 checks the same numbers against `sepiabox-ingest report`, S-062.)
- [x] Given the generated videos, when `process` runs, then their manifests have the locations the generator wrote.

### Technical notes
- Integration (FFmpeg); tiny images from `TestPhotos`; no binary committed.
- Decisions: D-012, D-046.

### Expected tests
- Integration: determinism, agreement with the manifests, video locations.
