## S-047 — Import an Apple Photos or iCloud Photos export
**Epic:** Import exports
**Depends on:** S-004, S-043, S-045, S-063
**Priority:** Medium
**Size:** M

### Context
iCloud Photos has no public API, so Apple photos come from exports (D-053). There are two official ways out: the Photos app on a Mac ("File → Export → Export Unmodified Originals", optionally with "Export IPTC as XMP", which writes an `.xmp` sidecar per file), and the iCloud data copy from privacy.apple.com, delivered as zip parts with the originals and CSV files of details and albums.

### Scope
- `AppleExportReader` (`--export apple-photos`) that reads whichever of the two layouts the folder holds:
  - **Photos app export:** originals (HEIC, JPEG, MOV, and Live Photo pairs of a still and a `.MOV`) and, when present, `.xmp` sidecars: caption (`dc:description`), title (`dc:title`, used as caption when there is no description), keywords are ignored, date (`photoshop:DateCreated` / `exif:DateTimeOriginal`) and GPS. Folder names from "Subfolder format: Moment name" are not albums.
  - **privacy.apple.com copy:** the `Photos/` folders and the `Photo Details*.csv` files, believed to have the columns `imgName`, `fileChecksum`, `favorite`, `hidden`, `deleted`, `originalCreationDate`, `viewCount`, `importDate`; `originalCreationDate` is the export date candidate. Album CSVs (an `Albums/` folder with one CSV per album listing image names) give the albums. Rows marked `deleted` are not media to import.
- `.AAE` edit sidecars are bookkeeping, not media.
- **No link at the source (D-088):** neither layout is believed to carry a per-item address, and iCloud Photos has no stable per-photo URL (**confirm against a real export**), so the sources have no `url`. In index-only mode (`process --export apple-photos --index-only`, S-063) the detail page says the original is stored in Apple Photos / iCloud Photos and can't be opened from here (S-064).

### Out of scope
- Downloading the export (human step: the Photos app on a Mac, or privacy.apple.com → "Request a copy of your data" → iCloud Photos).
- Joining a Live Photo's still and video into one asset; shared albums; Memories.
- Hidden and favourite flags (a later story may use them).

### Acceptance criteria
- [x] Given a synthetic Photos app export with `IMG_0001.HEIC`-like JPEG and an `.xmp` sidecar with a description and a date, when processed, then the manifest has the caption, and the date when the file has none.
- [x] Given a synthetic privacy.apple.com layout with a `Photo Details.csv` row for an undated file, then `capturedAt` comes from `originalCreationDate` with `capturedAtSource: export`.
- [x] Given an `Albums/Holidays.csv` listing two files, then both manifests have `albums: ["Holidays"]`.
- [x] Given a row with `deleted` set, then that file is left out and listed as such.
- [x] Given `.AAE` files, then they produce no manifest and no problem.
- [x] Given `process --export apple-photos --index-only`, then each manifest says `originalStored: false`, its source has no `url`, and `upload` sends no original.

### Technical notes
- **Format confirmation first:** Apple does not document these files. Before writing the reader, confirm the CSV columns, the date format of `originalCreationDate` and the album layout against a small real export the owner provides, and record what was found in the PR; adjust the reader and the fixtures to it. Fixtures stay synthetic (tests write the CSV, XMP and tiny images), never real personal data.
- XMP is XML: read it with `System.Xml` (no new dependency); MetadataExtractor may already read XMP embedded in files.
- Source `type` `apple-photos` (D-053).
- Decisions: D-037, D-046, D-053, D-088.

### Expected tests
- Unit: CSV parsing (quoted fields, the date format confirmed), XMP parsing, album lists.
- Integration: `process --export apple-photos` over each generated layout.

### Status
- `doing` (2026-10-22): implemented against the Apple formats as believed (D-109), with synthetic fixtures. **The real-export confirmation the story asks for first has not been done** (no export was available): it is the owner check in `docs/operations.md`, and the story is not `done` until it passes or the owner waives it (D-054).
