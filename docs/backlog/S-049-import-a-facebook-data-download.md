## S-049 — Import a Facebook data download
**Epic:** Import exports
**Depends on:** S-004, S-043, S-045, S-048
**Priority:** Medium
**Size:** S

### Context
Facebook's API does not expose a person's own photo archive, so photos and videos come from "Download your information" in JSON format (D-053). Like Instagram, Facebook strips EXIF from the media and the JSON uses the same conventions, so this reader reuses S-048's helpers.

### Scope
- `FacebookReader` (`--export facebook`) over the extracted download, reading the files believed to be at `your_facebook_activity/posts/album/<n>.json` (one per album: `name`, `photos[]` with `uri`, `creation_timestamp`, `title`, `description`, and `media_metadata.photo_metadata.exif_data[]` with `taken_timestamp`, `latitude`, `longitude`) and `your_facebook_activity/posts/your_posts__check_ins__photos_and_videos_1.json` (posts with `attachments[].data[].media` and the post's text under `data[].post`); older downloads have the same files without the `your_facebook_activity/` prefix.
- Date: `taken_timestamp` when present, otherwise `creation_timestamp`; caption: the media's `description`, else the post's text; albums: the album `name` ("Timeline Photos", "Mobile Uploads" and similar Facebook system albums included, as Facebook names them), plus "Facebook".
- Text repair and timestamp helpers from S-048 (`MetaExport`).
- **Link at the source (D-088):** the download is believed to carry no per-item address (**confirm against a real download**). If it has one, it becomes the source's `url` as in S-048; otherwise, in index-only mode (S-063), the detail page says the original is stored on Facebook (S-064).

### Out of scope
- Downloading (human step: Accounts Center → Download your information, format JSON, media quality high).
- Photos of the family uploaded by other people (not in the download), Messenger media, tags of people.

### Acceptance criteria
- [ ] Given a synthetic `album/0.json` named "Viagem a Paris" with one photo with `taken_timestamp`, then its manifest has that date (`capturedAtSource: export`), the album "Viagem a Paris" and "Facebook".
- [ ] Given a photo without `taken_timestamp`, then `creation_timestamp` is the date.
- [ ] Given a photo in an album and in a post, then its manifest has the album and the post's text as the caption when the media has no description.
- [ ] Given both layouts (with and without the `your_facebook_activity/` prefix), then the same manifests are written.

### Technical notes
- **Format confirmation first:** confirm the paths and fields against a small real download the owner provides before writing the reader, and record them in the PR. Fixtures stay synthetic, never real personal data.
- Source `type` `facebook` (D-053).
- Decisions: D-037, D-046, D-053, D-088.

### Expected tests
- Unit: album and post parsing, date and caption choice.
- Integration: `process --export facebook` over a generated download.
