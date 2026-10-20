## S-057 — Delete a source's assets from the library
**Epic:** Cloud catalog
**Depends on:** S-008, S-050, S-058
**Priority:** Medium
**Size:** M

### Context
Undoing a wrong upload (the wrong folder, a test run, a drive that should not be in the library) today means deleting blobs and rows by hand. A command that removes everything that came **only** from one named source does it safely. Manifests are shared, merged state (D-036), so deletion must never remove an asset that another source still needs, even when the catalog and the blob disagree or another upload is running.

### Scope
- `sepiabox-worker assets delete --source <name>`: a **dry run** by default that lists every matched asset with its source paths, the blobs it would delete and the totals; `--yes --expect <n>` deletes, and refuses to start unless exactly `n` assets match.
- An asset matches only when **every** source in the catalog **and** every source in its stored manifest has that name. Several sources with the same name (the same file at two paths of one drive) still match; one source with another name anywhere means kept.
- For each matched asset, in this order:
  1. Read the stored manifest through `ManifestStore` and validate it with `ManifestValidator` (D-026); a manifest that is missing, invalid or lists another source → kept and listed.
  2. Delete the manifest blob with `If-Match` on the ETag just read (`IBlobStore` conditional delete, S-050); a lost condition (someone uploaded meanwhile) → kept and listed, to retry later.
  3. Only after that succeeds, delete the derivatives (`derivatives/<id>/`) and the original; a blob already gone counts as deleted.
  4. Delete the catalog rows (asset, sources, derivatives, embeddings, place, faces and people links when those tables exist, manifest import records) in one transaction.
- After deleting the derivatives and the original, read the manifest again: if one has reappeared (an `upload` ran meanwhile), list the asset as "re-upload needed" with its source paths and exit 1, since its blobs are gone.
- A rerun finishes an interrupted deletion (an asset whose manifest is gone but whose rows or blobs remain).
- Runs in Azure as a manual Container Apps job `sepiabox-admin` (worker image) with its own managed identity: *Storage Blob Data Contributor* on the storage account and *Key Vault Secrets User* for the catalog connection. The import identity keeps read-only access (S-058).
- `docs/operations.md`: how to run it (dry run, then `--yes --expect`), that the local `upload` state is not changed, so re-uploading the same work folder needs its upload state reset for those assets (document how, adding an `upload` option if there is none), and that early deletion of Cold-tier originals is charged (90 days minimum), and that `upload` must not run while `assets delete --yes` runs.

### Out of scope
- Removing one source from an asset that keeps others (the stale-source follow-up, D-032).
- A web page for it.

### Acceptance criteria
- [ ] Given assets whose only source is `wrong-drive` and other assets, when `assets delete --source wrong-drive` runs without `--yes`, then it lists exactly the first group with their paths and blobs, and nothing changes.
- [ ] Given `--yes --expect 3` when 4 assets match, then nothing is deleted and it exits with the usage code.
- [ ] Given `--yes --expect <n>` with the right count, then the matched assets' catalog rows, manifests, derivatives and originals are gone (Azurite + PostgreSQL), other assets are unchanged, and catalog counts drop by exactly `n`.
- [ ] Given an asset whose catalog sources are all `wrong-drive` but whose stored manifest also lists `HD da Ana`, then it is kept and listed.
- [ ] Given a manifest changed between the read and the delete (a test that writes it in between), then the delete loses its condition, the asset is kept with all its blobs and rows, and a rerun handles it.
- [ ] Given an asset with two sources both named `wrong-drive` (two paths), then it is deleted.
- [ ] Given a manifest written again after the derivatives and original were deleted (a test that writes it at that moment), then the asset is listed as "re-upload needed" with its source paths and the exit code is 1.
- [ ] Given the generated Bicep, then `sepiabox-admin` has a manual trigger and its own identity with *Storage Blob Data Contributor*, and the import jobs' identity has no write role.

### Technical notes
- Deleting a manifest blob raises no import (Event Grid only forwards `BlobCreated`, S-033).
- Exit codes as `import` (D-034): 0 done, 1 some assets kept or failed (listed), 2 usage.
- Decisions: D-004, D-026, D-032, D-034, D-036, D-051, D-057.

### Expected tests
- Integration (Azurite + PostgreSQL): dry run, expect mismatch, delete, catalog/blob disagreement, lost ETag condition, duplicate sources, rerun after interruption. Bicep test for the admin job identity.
