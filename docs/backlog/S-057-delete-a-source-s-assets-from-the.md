## S-057 — Delete a source's assets from the library
**Epic:** Cloud catalog
**Depends on:** S-008, S-012
**Priority:** High
**Size:** S

### Context
The MVP acceptance (S-054) uploads a generated fixture into the same library the family uses; it must leave nothing behind. A command that removes everything that came only from one named source is also the safe way to undo a wrong upload.

### Scope
- `sepiabox-worker assets delete --source <name>`: finds the assets whose **only** source has that name and, by default, only lists them (count, bytes, first ids) as a dry run; with `--yes` it deletes their catalog rows (asset, sources, derivatives, embeddings, place, faces and people links when those tables exist, manifest import records), their derivative blobs (`derivatives/<id>/`), their manifest blobs and their originals.
- Assets that also have another source are listed as kept and left untouched.
- Blob deletion is new code behind a small interface declared in the worker (D-051), with the Azure implementation registered in `Program.cs`.
- Exit codes as `import` (D-034): 0 done, 1 some deletions failed (listed), 2 usage.

### Out of scope
- Removing one source from an asset that keeps others (the stale-source follow-up, D-032).
- A web page for it.

### Acceptance criteria
- [ ] Given assets with only source `mvp-acceptance`, an asset with `mvp-acceptance` and `HD da Ana`, and other assets, when `assets delete --source mvp-acceptance` runs without `--yes`, then it lists the first group, lists the shared one as kept, and changes nothing.
- [ ] Given the same with `--yes`, then the first group's catalog rows, derivatives, manifests and originals are gone (Azurite + PostgreSQL), the shared asset and the others are unchanged, and catalog counts drop by exactly the listed number.
- [ ] Given a deletion that fails for one blob, then the others are still deleted, the failure is listed and the exit code is 1; a second run finishes the job.

### Technical notes
- Delete the catalog rows in one transaction, then the blobs; a blob already gone counts as deleted.
- Deleting a manifest blob raises no import (Event Grid only forwards `BlobCreated`, S-033).
- Originals are in the Cold tier, which charges early deletion (90 days minimum); for a few small fixture files that is a fraction of a cent.
- Decisions: D-004, D-032, D-034, D-051.

### Expected tests
- Integration (Azurite + PostgreSQL): dry run, delete, shared asset kept, partial failure.
