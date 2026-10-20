## S-038 — Sync Google Drive photos in reference mode
**Epic:** Cloud sources
**Depends on:** S-037, S-036
**Priority:** Medium
**Size:** M

### Context
With the account connected, Drive photos appear alongside the HD ones, without duplicating originals (D-023).

### Scope
- Daily scheduled job `sepiabox-worker sync google-drive`: the first run lists everything; subsequent runs use the changes API (stored `startPageToken`).
- Each new file is downloaded to `incoming` with metadata (source google-drive, fileId, path, reference mode).
- File removed in Drive → source marked as removed.
- Same content already from the HD → only gains a source (no new asset).
- Photo detail: "Open original" downloads from Drive through the server.

### Out of scope
- Google Photos (restricted API).

### Acceptance criteria
- [ ] Given a fake client with 5 files, when the first sync runs, then 5 blobs land in `incoming`.
- [ ] Given a second sync with 1 change, then only 1 blob lands.
- [ ] Given a removed file, then the source is marked as removed.
- [ ] Given content identical to an HD asset, then the asset gains a source and is not duplicated.

### Technical notes
- Container Apps Job cron in the AppHost.
- Sources are add-only (D-036): a file deleted or moved in Drive keeps its old source until a removal mechanism exists. If sync needs it, add a `removedAt` tombstone to `ManifestSource`, merged like `modifiedAt` (newer wins), and have the import mark the source as removed instead of deleting it.
- Manifests written by the sync go through `ManifestStore.WriteMergedAsync` (D-036).
- Decisions: D-023, D-024, D-036.

### Expected tests
- Integration (Azurite + PostgreSQL) with a fake Drive client.
