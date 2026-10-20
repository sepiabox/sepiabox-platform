## S-045 — Read metadata from photo exports during process
**Epic:** Import exports
**Depends on:** S-004, S-043
**Priority:** Medium
**Size:** M

### Context
Google Photos, Apple Photos / iCloud, Instagram and Facebook are imported from their official exports, processed locally by `sepiabox-ingest` (D-053). Each export carries metadata next to the media (JSON sidecars, CSV files, post lists) with dates, places, captions and albums that the files themselves often lack. This story adds the shared mechanism; S-046 to S-049 add one reader per export.

### Scope
- `process <folder> --export <kind> [--time-zone <IANA id>]`: `<kind>` names a registered `IExportReader`; without `--export`, `process` behaves exactly as today.
- `IExportReader` (Sepiabox.Pipeline, pure over the folder, read-only): given the export root, returns `ExportMetadata` per media file (relative path → capture time as UTC or wall-clock, location, caption, album names) and the files to leave out as export bookkeeping (JSON, CSV, HTML). Readers never open a file for writing (CLAUDE.md).
- Capture date precedence (D-053, extends D-046): the file's metadata, then the export, then the file name, then the folder; new `capturedAtSource` value `export`. UTC export times become wall-clock time in `--time-zone` (default: the computer's zone), never stored with an offset (D-037).
- Location: the export's location fills `location` only when the file has none; coordinates `0,0` count as none.
- Manifest (schema stays v1, optional fields as in D-046/D-047): `caption` (string, 1–2,000 characters) and `albums` (array of distinct non-empty strings). Source `type` is the export kind; source `name` is the drive label as today.
- Merge (D-036): `albums` is the union, sorted ordinally; `caption` keeps the longer one, then the ordinally greater, so `Merge(a, b)` equals `Merge(b, a)`; `capturedAtSource` ranks `metadata` > `export` > `fileName` > `folder`.
- A test-only reader in the tests exercises the mechanism; no real export format is read in this story.

### Out of scope
- The readers themselves (S-046–S-049).
- Showing captions and albums, and filtering by album (S-052).
- Near-duplicates: a photo re-encoded by Instagram or Facebook has other bytes, so it is a separate asset (bursts and similar photos are S-027 and S-017).

### Acceptance criteria
- [ ] Given a fake reader that dates an undated file `2015-06-01T10:00:00Z` and `--time-zone Europe/Lisbon`, when `process` runs, then `capturedAt` is `2015-06-01T11:00:00` and `capturedAtSource` is `export`.
- [ ] Given a file with an EXIF date and a different export date, then the EXIF date is kept with `capturedAtSource: metadata`.
- [ ] Given a file without metadata date in a `YYYY-MM-DD` folder and an export date, then the export date wins over the folder.
- [ ] Given a file without GPS and an export location, then `location` is the export's; given a file with GPS, then its own GPS is kept.
- [ ] Given a caption and two albums from the reader, then the manifest has `caption` and `albums`, and the schema validates it; a manifest without them still validates and imports.
- [ ] Given two manifests of the same asset with different albums and captions, when merged in either order, then the stored JSON is the same.
- [ ] Given `--export` with an unknown kind, then `process` exits with the usage code and lists the known kinds.
- [ ] Given the export folder, when `process` runs, then no file in it is modified (modification times and sizes unchanged).

### Technical notes
- Keep `CaptureDate.Resolve` the one place that decides the date (D-046); the export date is one more candidate.
- `TimeZoneInfo.FindSystemTimeZoneById` with IANA ids; `Sepiabox.Ingest` keeps invariant globalization, which does not affect time zones.
- The catalog ignores `caption` and `albums` until S-052; the importer must accept them (schema-valid).
- Update `docs/schemas/manifest.v1.json`, `docs/operations.md` (how to run `process --export`), and record the merge rules in a new decision.
- Decisions: D-005, D-014, D-036, D-037, D-046, D-053.

### Expected tests
- Unit: precedence with the export candidate, time-zone conversion, location fill, merge symmetry, schema with and without the new fields.
- Integration: `process --export` with a test reader over a generated folder writes the expected manifests and leaves the folder unchanged.
