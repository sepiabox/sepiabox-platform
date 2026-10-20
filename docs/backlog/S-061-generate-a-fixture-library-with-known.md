## S-061 — Generate a fixture library with known contents
**Epic:** Questions
**Depends on:** S-004, S-053
**Priority:** High
**Size:** S

### Context
The MVP acceptance test (S-054) needs a folder of photos and videos whose counts per year, kind, place and source are known in advance, generated in the test rather than committed (CLAUDE.md: generate larger media in the test).

### Scope
- `FixtureLibrary` in `tests/Shared`: writes a folder with photos (EXIF dates, some with GPS in Lisbon and Porto), phone-style videos with QuickTime creation dates and ISO 6709 locations (S-053, through FFmpeg), undated files in `YYYY-MM-DD` folders (D-046), files without GPS, an exact duplicate, and a non-media file; it returns the expected numbers (per year, per kind, per city, per source, duplicates) computed from what it wrote.
- Deterministic: a seed gives the same files and numbers.

### Out of scope
- Running the pipeline over it (S-054).

### Acceptance criteria
- [ ] Given a seed, when the library is generated twice, then the files and the expected numbers are identical.
- [ ] Given a generated library, when `scan --report` runs on it, then its totals by year and kind equal the generator's expected numbers.
- [ ] Given the generated videos, when `process` runs, then their manifests have the locations the generator wrote.

### Technical notes
- Integration (FFmpeg); tiny images from `TestPhotos`; no binary committed.
- Decisions: D-012, D-046.

### Expected tests
- Integration: determinism, `scan` agreement, video locations.
