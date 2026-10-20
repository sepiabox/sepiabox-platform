## S-053 — Read video locations from QuickTime metadata
**Epic:** Local ingest
**Depends on:** S-004
**Priority:** High
**Size:** S

### Context
`MetadataReader` reads GPS only from EXIF. Phone videos (MP4/MOV from iPhones and Android phones) keep their location in QuickTime metadata instead: an ISO 6709 string such as `+38.7223-009.1393+010.000/` in the `©xyz` user-data atom (Android, older iPhones) or the `com.apple.quicktime.location.ISO6709` metadata key (iPhones). Without it, videos have no place, and "videos from Lisbon" misses them. The owner has not run the full `process` over the drive yet, so this should land before that run.

### Scope
- Read the ISO 6709 location of MP4/MOV files in `MetadataReader`: from MetadataExtractor's QuickTime directories (user data and metadata header) when they expose it, otherwise from ffprobe's format tags (`location`, `com.apple.quicktime.location.ISO6709`) during the video probe `process` already runs.
- A pure `Iso6709` parser: latitude and longitude with sign, decimal degrees with optional altitude and trailing `/`; anything else (degrees-minutes forms, out-of-range values, `+00.0000+000.0000`) is ignored.
- Refresh of files already processed: `process` records a metadata version in its state; files processed with an older version get only their headers re-read (no derivatives, no full hash of unchanged files) and their manifests rewritten through the work folder, so a second run after this story updates earlier runs cheaply.

### Out of scope
- Reverse geocoding (S-012).
- Locations of camcorder MPEG files (they do not carry one).

### Acceptance criteria
- [ ] Given `+38.7223-009.1393+010.000/`, then the parser returns 38.7223, −9.1393; given `+00.0000+000.0000/` or `+95.0000+000.0000/`, then no location.
- [ ] Given a generated MP4 with a `location` tag (FFmpeg `-metadata location=...`) and a MOV with `com.apple.quicktime.location.ISO6709`, when `process` runs, then each manifest has that `location`.
- [ ] Given a photo with EXIF GPS, then its location is unchanged.
- [ ] Given a work folder processed before this story, when `process` runs again on the same drive, then the videos' manifests gain their location without regenerating derivatives (the derivative files' write times are unchanged).

### Technical notes
- Test media is generated in the test with FFmpeg (Integration), never committed.
- Check `scan` and `process` share the reader, as for dates (D-046).
- Decisions: D-014, D-029, D-046.

### Expected tests
- Unit: `Iso6709` theory. Integration: generated MP4 and MOV; the metadata-version refresh.
