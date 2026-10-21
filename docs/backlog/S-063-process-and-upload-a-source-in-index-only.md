## S-063 — Process and upload a source in index-only mode
**Epic:** Local ingest
**Depends on:** S-005, S-006, S-007, S-008, S-015, S-018, S-040, S-051
**Priority:** Medium
**Size:** M

### Context
The family wants to search libraries whose originals already live somewhere else (the external drive, a Google Takeout, an iCloud copy, an Instagram download) without copying those originals into Sepiabox (D-088). In **index-only mode** the originals stay where they are and are never uploaded. Everything else goes to the cloud as usual: thumbnails, previews, embeddings, faces, metadata and manifests. The catalog records that the original is not stored and where it lives. The previews are larger, so they can stand in for the original on screen. This is D-023's reference mode, available for any source that `process` reads.

### Scope
- **The option.** `sepiabox-ingest process <folder> --out <work folder> --index-only`, named like the other `process` options (`--faces`, `--retry-left-out`).
  - The mode belongs to the work folder. The first `process` run records it in the work folder's state (`state.db`), and every later run on that `--out` follows it.
  - A later `process` run on the same `--out` that asks for the other mode stops before reading any file, with the usage exit code and the message "This work folder is index-only (or: keeps originals); use a new --out for the other mode." This avoids mixing two preview sizes and two upload rules in one folder.
  - `upload` has no option of its own: it follows the mode the work folder recorded.
  - Combines with every other `process` option, including `--export <kind>` (S-045) and `--faces`.
- **Larger previews in index-only mode.**
  - Photos get `preview-2048`: WebP, long side 2048 px, never upscaled, the same quality setting as `preview-1280`. It replaces `preview-1280` in that work folder.
  - Videos get `proxy-1080p`: H.264, fitted inside 1920×1080 in either orientation, sides even, never upscaled, with the same encoder settings and sound rules as the 720p version (D-029, D-084, D-087). It replaces `proxy-720p`.
  - `thumb-256` and the video `poster` (1280) are unchanged.
  - Embeddings (S-015) and faces (S-018) read the largest preview the manifest lists (`preview-2048`, else `preview-1280`, else the poster), so they work in both modes. Faces' boxes stay relative (0..1), so a 2048 preview needs no change to them.
- **Manifest** (schema stays v1; only optional fields are added, as D-046 and D-047 did):
  - `originalStored` (boolean): `false` when the work folder is index-only. Absent means `true`, so manifests written before this story keep their meaning.
  - On each source: optional `url` (an `https://` address, at most 2,000 characters): the original's address at its source, when the source provides one. This story only writes the field and carries it through. The export readers fill it in (S-046, S-048).
  - A source's location is otherwise what it already is: `type` (`local`, an export kind of D-053, or a cloud kind of D-023) with `name` (the drive's or export's label given by `--source-name`) and `path`.
  - **Merge (D-036):** `originalStored` is `true` when either side is true (absent counts as true), so a drive uploaded in full mode keeps its original in the catalog even after an index-only copy is uploaded. A source's `url` merges like its other fields: the newer `modifiedAt` wins, and a present url beats an absent one. `Merge(a, b)` and `Merge(b, a)` give the same JSON.
- **`upload` in an index-only work folder.**
  - It never opens a source file and never writes to the `originals` container. It uploads derivatives and manifests as today (manifests through `ManifestStore.WriteMergedAsync`, D-036).
  - It does not need the drive or the export to be connected. That is unlike full mode, which reports "Original not found. Connect the drive …".
  - The summary adds one count: "originals left at their source N".
- **Catalog.**
  - A migration adds `assets.original_stored` (boolean, not null, default `true` for existing rows) and `asset_sources.url` (text, null).
  - The importer maps both. The original stays stored once any manifest says so.
- **Full export (S-040).** `originals.txt` lists only the originals Sepiabox stores. `catalog.json` carries each asset's `originalStored` and each source's `url`, so an export says where every index-only original lives.

### Switching a source between modes
- **Index-only → keeping originals: supported**, with no new code.
  1. Run `process` on the same source into a new work folder without `--index-only`.
  2. Run `upload` from that folder.

  The merged manifest then says `originalStored: true`, so the original is in storage, and the derivatives of both runs are kept by name (`preview-1280` and `preview-2048` side by side). This is the rule of D-088, documented in `operations.md`.
- **Keeping originals → index-only (removing originals already in storage): out of scope.** It would delete blobs, and S-057 deletes whole assets only. It is a later story if the family asks for it.

### Out of scope
- What the detail page shows instead of "Download the original" (S-064).
- Reading each export's per-item link (S-046, S-048).
- The Google Photos Picker, or any API access to Google Photos, iCloud or Instagram (D-053, D-088).

### Acceptance criteria
- [ ] Given a generated folder with a 4000×3000 photo and a 1920×1080 video, when `process --index-only` runs, then the manifests list:
  - for the photo, `thumb-256` and `preview-2048` at 2048×1536;
  - for the video, `poster`, `thumb-256` and `proxy-1080p` at 1920×1080;
  - no `preview-1280` and no `proxy-720p`;
  - `originalStored: false`;

  and the schema validates them.
- [ ] Given a 1000×750 photo, then `preview-2048` is 1000×750 (no upscaling).
- [ ] Given an index-only work folder, when `process` runs on it again without `--index-only`, then it stops before reading any file, with the usage exit code and the message, and the work folder is unchanged; and the same the other way round.
- [ ] Given an index-only work folder, when `upload` runs with the source folder deleted, then derivatives and manifests are uploaded, nothing is written to `originals`, there is no problem, and the summary counts the originals left at their source.
- [ ] Given an asset uploaded in full mode and the same bytes later uploaded from an index-only work folder (and the other order), then the stored manifest and the catalog say the original is stored, and the asset lists both sources.
- [ ] Given a manifest whose source has a `url`, when it is imported, then `asset_sources.url` holds it; a manifest without `originalStored` imports as stored.
- [ ] Given a model folder and `--faces`, when `process --index-only` runs, then the embedding and the faces are computed from `preview-2048`.
- [ ] Given an index-only asset, when the full export (S-040) runs, then `originals.txt` has no line for it and `catalog.json` lists its sources with their `url`.

### Technical notes
- **Names.** `ThumbnailGenerator` gains a size list for index-only (`preview-2048`). `VideoScale.Fit` already takes `longest`/`shortest` (1920/1080). `DerivativeNames` (web) learns the two new names; S-064 shows them.
- **Storage estimate** (rough, Hot LRS about €0.02/GB a month in France Central; confirm in the Azure pricing calculator). The full figures and the ceiling comparison are in D-088.
  - Photos: a 2048 px WebP preview is about 0.5 MB, so about **5 GB per 10,000 photos (about €0.10 a month)**. For comparison, full mode's 1280 px previews take about 2 GB per 10,000 plus the originals, about 30–50 GB in Cold.
  - Videos: the 1080p version at CRF 28 is about 3–4 Mbit/s, so about **1.5–2 GB per hour (about €0.04 a month)**, against about 0.7 GB per hour for 720p.
  - So 100,000 photos and 100 hours of video come to about 230 GB, about €5 a month: a tenth of the €50 ceiling (D-008).
- **Upload state.** No work-folder files are added beyond the previews. The upload state's fingerprint rule is unchanged.
- **Schema.** Update `docs/schemas/manifest.v1.json`: `originalStored` at the root and `url` on sources, with the pattern `^https://` and a maximum length.
- Record the merge rule in D-088 if the implementation refines it, and describe `--index-only` and switching modes in `operations.md`.
- Decisions: D-004, D-008, D-015, D-023, D-029, D-036, D-046, D-051, D-053, D-087, D-088.

### Expected tests
- **Unit:**
  - option parsing;
  - preview and 1080p sizes (orientation, no upscaling, even sides);
  - the merge of `originalStored` and `url` in both orders;
  - the schema with and without the new fields.
- **Integration** (FFmpeg; Azurite + PostgreSQL):
  - `process --index-only` over a generated folder;
  - the mode guard on a second run;
  - `upload` with the source folder removed;
  - an asset uploaded from a full work folder and from an index-only one, in both orders, imported into the catalog.
