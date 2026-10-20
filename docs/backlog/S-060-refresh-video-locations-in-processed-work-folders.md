## S-060 — Refresh video locations in processed work folders
**Epic:** Local ingest
**Depends on:** S-053
**Priority:** Low
**Size:** S

### Context
S-053 reads phone video locations from QuickTime metadata. It landed before the owner's first full `process` run, so the MVP needed no refresh. A work folder processed before S-053 (another computer, a later drive, a test run) keeps its videos without a location unless they are processed again, and re-hashing a large drive just for a location is wasteful.

### Scope
- `process` records a metadata version per file in its state; files processed with an older version get only their headers re-read (no derivatives regenerated, no full re-hash of files whose size and modification time are unchanged), and their manifests are rewritten in the work folder; the next `upload` sends the changed manifests (D-036 merge).
- The run's summary counts the refreshed files.

### Out of scope
- Refreshing manifests already uploaded without the work folder.

### Acceptance criteria
- [ ] Given a work folder processed with the previous metadata version, when `process` runs again on the same drive, then the videos' manifests gain their location and the derivative files' write times are unchanged.
- [ ] Given an unchanged file already at the current version, then its headers are not read again.

### Technical notes
- Same "only what changed" approach as embeddings and faces (D-042, D-047): the work folder is the source of truth.
- Decisions: D-036, D-042, D-046.

### Expected tests
- Integration: a generated MP4 processed with the old version, then refreshed.
