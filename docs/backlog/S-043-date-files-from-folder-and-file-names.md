## S-043 — Date files from folder and file names
**Epic:** Local ingest
**Depends on:** S-004
**Priority:** High
**Size:** M

### Context
The owner's archive (`scan` of `E:\Life`, 87,891 files) has 4,556 photos and videos with no date: mostly MPEG program and transport streams (`.mpg`, `.mts`, camcorder `.mod`), whose dates MetadataExtractor cannot read. Without a date, they all fall into "Sem data" at the end of the timeline. The files sit in folders named after the capture day and are named after the capture time, `E:\Life\2011-09-25\MNU_110425_<guid>.MTS`; the owner confirmed the folders reflect capture dates. Phones use `IMG_20120304_101112.jpg`, `VID_20120304_101112.mp4` or `20120304_101112.mp4`.

### Scope
- In Pipeline, a `PathDate` parser (pure, no I/O) that returns a date and time from a file path, or nothing:
  - the file name contains `YYYYMMDD_HHMMSS` or `YYYYMMDD-HHMMSS` (any prefix such as `IMG_`, `VID_`, `PXL_`) → that date and time;
  - the parent folder is named `YYYY-MM-DD` and the file name starts with `MNU_HHMMSS` → folder date with that time;
  - the parent folder is named `YYYY-MM-DD` → that date at 00:00:00;
  - anything that is not a valid calendar date or time (`2011-02-30`, `25:61:00`) is ignored.
- Precedence: a date read from the file's own metadata (EXIF, QuickTime) always wins; the path is only a fallback.
- Implausible metadata dates (before 1990-01-01, or more than one day after the time of processing, e.g. a camera with a reset clock) are treated as missing, so the path fallback applies.
- `process` writes the fallback date to `capturedAt` and a new optional manifest field `capturedAtSource`: `metadata`, `fileName` or `folder` (omitted when `capturedAt` is null). Update `docs/schemas/manifest.v1.json` (still v1: the field is optional).
- Merging manifests (D-036): a date from `metadata` is never replaced by one from the path; between path dates, the incoming one wins.
- `scan` uses the same rules for "by year", so its report matches what `process` will record.

### Out of scope
- Showing in the app that a date is approximate, and storing `capturedAtSource` in the catalog (a later story can read it from the manifests).
- Dates from ffprobe `creation_time` (MPEG-PS/TS camcorder files do not carry it).
- Re-dating files already processed (the owner has only run `scan`).
- Time zones: `capturedAt` stays wall-clock time (D-037).

### Acceptance criteria
- [ ] Given `E:\Life\2011-09-25\MNU_110425_d8b2.MTS` with no metadata date, when it is processed, then `capturedAt` is `2011-09-25T11:04:25` and `capturedAtSource` is `fileName`.
- [ ] Given `2008-12-24\clip.mod` with no metadata date, then `capturedAt` is `2008-12-24T00:00:00` and `capturedAtSource` is `folder`.
- [ ] Given `VID_20120304_101112.mp4` with no metadata date, then `capturedAt` is `2012-03-04T10:11:12`.
- [ ] Given a JPEG with an EXIF date in a `YYYY-MM-DD` folder for another day, then the EXIF date is kept and `capturedAtSource` is `metadata`.
- [ ] Given a JPEG with EXIF date 1980-01-01 in folder `2005-07-14`, then `capturedAt` is `2005-07-14T00:00:00`.
- [ ] Given folder `2011-02-30`, then no date comes from the path.
- [ ] Given a scan of a folder `2009-05-01` with an undated `.mpg`, then "by year" counts it in 2009.
- [ ] Given an old manifest without `capturedAtSource`, then it still validates and imports.

### Technical notes
- Use the parent folder only, not other ancestors: a dated folder higher up says less about the file.
- Parse with `DateTime.TryParseExact` and the invariant culture.
- Keep `MetadataReader` as is; combine its result with `PathDate` in one place used by both `scan` and `process`.
- Decisions: D-014, D-036, D-037. Record the precedence and the plausibility window as a new decision.

### Expected tests
- Unit: `PathDate` theory with every pattern, invalid dates, Windows and POSIX separators; precedence and plausibility; merge rule; schema with and without `capturedAtSource`.
- Integration: `process` on a generated undated `.mpg` in a `YYYY-MM-DD/MNU_HHMMSS_x.mpg` path writes the expected manifest.
