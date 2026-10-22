## S-046 — Import a Google Takeout export of Google Photos
**Epic:** Import exports
**Depends on:** S-004, S-043, S-045, S-063
**Priority:** Medium
**Size:** M

### Context
The Google Photos Library API no longer reaches the whole library (since 2025 it only sees items the app created), so Google Photos comes from Google Takeout (D-053). Takeout delivers the originals plus one JSON sidecar per file with the dates, places and descriptions that Google Photos keeps, which are often missing from the files themselves (for example images received through messaging apps).

### Scope
- `GoogleTakeoutReader` (`--export google-takeout`) over the extracted export (the `Takeout/Google Photos/` folder, or a folder of several extracted `takeout-*.zip` parts merged together).
- Per media file, find its sidecar and read: `photoTakenTime.timestamp` (UTC epoch seconds, as a string) as the capture time; `geoData` (or `geoDataExif`) latitude and longitude, where `0.0, 0.0` means none; `description` as the caption. `title` is the original file name and is not used as a caption.
- Albums: each album folder's name, or the `title` in its `metadata.json` when present; the `Photos from YYYY` folders are not albums. A file in several album folders is the same bytes, so the same asset collects all its albums (S-045 merge).
- Sidecar matching rules for the known quirks: `<name>.json` and, in newer exports, `<name>.supplemental-metadata.json` (possibly truncated); long names truncated in the sidecar name; duplicates `IMG(1).jpg` with the sidecar `IMG.jpg(1).json`; edited copies `<name>-edited.jpg` (in the export's language) sharing the original's sidecar. A media file without a sidecar is processed with its own metadata only and counted in one warning.
- **Link at the source (D-088):** the sidecar's `url` (believed to be the item's `https://photos.google.com/photo/…` address; **confirm against a real export**) becomes the source's `url` (S-063), when it is an `https://` address; otherwise the source has none. In index-only mode (`process --export google-takeout --index-only`, S-063) the original stays in the Takeout folder, and the detail page links to Google Photos (S-064).
- `metadata.json`, the sidecars and `print-subscriptions.json`/`shared_album_comments.json`-like files are export bookkeeping, not media.

### Out of scope
- Downloading Takeout (a human step: takeout.google.com, choose Google Photos, JSON is the default format).
- People names in the sidecars (`people[].name`): face tags belong to the People epic and are opt-in biometric-adjacent data; left for later.
- Motion photos as one asset: the still and its short video are separate files, processed as a photo and a video.

### Acceptance criteria
- [x] Given a synthetic export with `IMG_0001.jpg` (no EXIF date) and `IMG_0001.jpg.json` with `photoTakenTime.timestamp` "1561975200", when `process --export google-takeout --time-zone Europe/Lisbon` runs, then `capturedAt` is `2019-07-01T11:00:00` and `capturedAtSource` is `export`.
- [x] Given a sidecar with `geoData` 38.7223, −9.1393 and a file without GPS, then the manifest `location` is that point; given `geoData` 0.0, 0.0, then no location comes from the export.
- [x] Given a sidecar `description` "Grandma's birthday", then the manifest `caption` is that text.
- [x] Given the same file in `Photos from 2019/` and `Summer in Portugal/` (with a `metadata.json` titled "Summer in Portugal"), then its manifest has `albums: ["Summer in Portugal"]`.
- [x] Given each sidecar naming quirk in Scope (supplemental, truncated, `(1)` duplicate, `-edited`), then the right sidecar is matched.
- [x] Given a media file without a sidecar, then it is processed with its own metadata and reported once in a warning.
- [x] Given a sidecar with `url` "https://photos.google.com/photo/AF1Qip-example", then the manifest's `google-takeout` source has that `url`; given no `url`, or one that is not `https://`, then the source has none.
- [x] Given `process --export google-takeout --index-only`, then the manifest says `originalStored: false`, keeps the source `url`, and `upload` sends no original.

### Technical notes
- **Format confirmation first:** sidecar names and fields have changed over the years. Before writing the reader, confirm them against a small real Takeout the owner provides (a few photos and one album), and record in the story's PR which variants were seen; the committed fixtures stay synthetic, never real personal data.
- Fixtures: generated in the test (tiny JPEGs from `TestPhotos` plus hand-written JSON), no binary export committed.
- Source `type` `google-takeout` (D-053). The `url` is the item's address in Google Photos; Sepiabox never calls the Google Photos API (D-053, D-088).
- Decisions: D-037, D-046, D-053, D-088.

### Expected tests
- Unit: sidecar matching theory, timestamp and geo parsing, album naming.
- Integration: `process --export google-takeout` over a generated export writes the expected manifests.

### Status
- `doing` (2026-10-22): implemented against the Takeout formats as known (D-106), with synthetic fixtures. **The real-export confirmation the story asks for first has not been done** (no export was available): it is the owner check in `docs/operations.md`, and the story is not `done` until it passes or the owner waives it (D-054).
