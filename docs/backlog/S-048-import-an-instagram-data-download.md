## S-048 — Import an Instagram data download
**Epic:** Import exports
**Depends on:** S-004, S-043, S-045
**Priority:** Medium
**Size:** M

### Context
The Instagram API does not expose a person's own archive, so posts come from "Download your information" in JSON format (D-053). Instagram strips EXIF from the media, so the export's JSON is the only source of dates, captions and, sometimes, places.

### Scope
- `InstagramReader` (`--export instagram`) over the extracted download, reading the post lists believed to be at `your_instagram_activity/content/posts_1.json` (older downloads: `content/posts_1.json`) and, when present, `archived_posts.json`, `stories.json` and reels: each post has `media[]` with `uri` (relative path to the file under `media/`), `creation_timestamp` (UTC epoch seconds) and `title`; single-media posts may keep `title` and `creation_timestamp` on the post, carousels on each media item; `media_metadata` may hold `photo_metadata.exif_data[]` or `video_metadata.exif_data[]` with `latitude`, `longitude` and `date_time_original`.
- Date: `date_time_original` when present, otherwise `creation_timestamp` (the posting time, an approximation of the capture date) as the export candidate.
- Caption: the post's or media's `title`. Albums: "Instagram" plus "Instagram stories" / "Instagram reels" for those lists, so the family can find them.
- Text repair: Meta's JSON writes UTF-8 bytes as `\u00XX` escapes (mojibake such as "aniversÃ¡rio"); decode strings by reinterpreting them as Latin-1 bytes and then UTF-8, only when that round trip is valid.
- Media files not listed in any JSON (profile pictures, messages) are left out of this export kind and counted in one line.

### Out of scope
- Downloading (human step: Accounts Center → Your information and permissions → Download your information, format JSON, media quality high).
- Comments, likes, messages and other people's media.

### Acceptance criteria
- [ ] Given a synthetic `posts_1.json` with one post, `creation_timestamp` 1561975200 and `title` "Praia", when processed with `--time-zone Europe/Lisbon`, then the manifest has `capturedAt` `2019-07-01T11:00:00`, `capturedAtSource: export`, `caption` "Praia" and `albums: ["Instagram"]`.
- [ ] Given a carousel post with three media, then each media file gets the post's caption and its own timestamp when present.
- [ ] Given `exif_data` with latitude, longitude and `date_time_original`, then they are used for the location and date.
- [ ] Given the escaped string "aniversÃ¡rio", then the caption is "aniversário"; a string that is not mojibake is left as it is.
- [ ] Given a media file under `media/` that no JSON lists, then it produces no manifest and is counted in the summary.

### Technical notes
- **Format confirmation first:** Meta changes the folder layout and field names between versions. Before writing the reader, confirm them against a small real download the owner provides, and record the paths and fields found in the PR. Fixtures stay synthetic (tests write the JSON and tiny media), never real personal data.
- Share the JSON helpers (timestamps, text repair, `exif_data`) with S-049 in one place (`MetaExport` in Sepiabox.Pipeline).
- Source `type` `instagram` (D-053).
- Decisions: D-037, D-046, D-053.

### Expected tests
- Unit: post parsing variants, text repair, date choice.
- Integration: `process --export instagram` over a generated download.
