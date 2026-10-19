## S-002 — Scan a drive and report what it contains
**Epic:** Local ingest
**Depends on:** S-001
**Priority:** High
**Size:** M

### Context
First contact with the family's external drive: find out how many photos and videos there are, from which years, how many duplicates and whether there are unreadable files (a sign of a failing disk), without changing anything. Implemented in PR sepiabox/sepiabox-platform#1 (`sepiabox-ingest scan`).

### Scope
- `sepiabox-ingest scan <folder> [--report <file.json>] [--no-duplicates]`.
- Classification by extension (`MediaTypes`), capture date (`CaptureDateReader`, MetadataExtractor), exact duplicates (`DuplicateFinder`: size → first/last 64 KB → full SHA-256).
- Skips system folders (`$RECYCLE.BIN`, `System Volume Information`, `.Trashes`, `@eaDir`…) and `._` files.
- Console summary and JSON report.

### Out of scope
- Generating manifests or thumbnails (S-004, S-005).

### Acceptance criteria
- [ ] Given a folder with photos, videos, other files and a `$RECYCLE.BIN` folder, when I run `scan`, then the totals per kind exclude the recycle bin.
- [ ] Given a photo with a `DateTimeOriginal` from 2019, when I run `scan`, then it counts towards 2019.
- [ ] Given two identical copies, when I run `scan`, then there is 1 duplicate group with 1 extra copy.
- [ ] Given an unreadable file, when I run `scan`, then it appears under "Problems" and the scan continues.
- [ ] Given `--report`, when the scan finishes, then the JSON contains every duplicate path.

### Technical notes
- Already implemented in PR sepiabox/sepiabox-platform#1 (`claude/ingest-scan`). To finish: review, make sure CI is green, merge and mark `done`.

### Expected tests
- Unit: classification, date reading (JPEG fixtures), duplicates, options, report. Integration: scanning a temporary folder.
