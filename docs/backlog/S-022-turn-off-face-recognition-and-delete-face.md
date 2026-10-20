## S-022 — Turn off face recognition and delete face data
**Epic:** People
**Depends on:** S-019
**Priority:** Medium
**Size:** S

### Context
Faces are biometric data (LGPD/GDPR). The family must be able to turn it off and delete it.

### Scope
- `/definicoes`: library face recognition toggle (confirmation on the screen itself).
- Turning it off deletes all rows in `faces` and `people`; with recognition off, import ignores faces in manifests.
- Per person: "Apagar dados de rosto" ("Delete face data") removes the person and their faces (the photos stay).
- Turning it off also removes faces from the stored manifest blobs, or the next import (or turning it back on) would bring them back. The erase goes through `ManifestStore.WriteMergedAsync` (D-036), never a direct upload, and must win the merge: an explicit erase rule such as a `facesErasedAt` tombstone (newer than the faces wins, merged like `modifiedAt`) or a dedicated erase writer that sets `facesProcessed: false` and `faces: []` and is never undone by an older `facesProcessed: true` manifest from a work folder (D-047).
- Say on the screen and in `docs/operations.md` how to clear faces from a `process` work folder too: delete the work folder, or run `process` into a fresh one without `--faces`, before any further `upload`; otherwise uploading it brings the faces back.

### Out of scope
- Per-user encryption.

### Acceptance criteria
- [ ] Given recognition turned off, then `faces` and `people` are empty.
- [ ] Given recognition turned off, when I import a manifest with faces, then no face is stored.
- [ ] Given "Apagar dados de rosto" on a person, then their faces disappear and the photos remain.

### Technical notes
- Table `settings` (key/value). The worker's face opt-in for files that arrive in the cloud (S-036) reads this setting; it is off by default (D-047).
- `ManifestMerge` today keeps faces from the side with `facesProcessed: true` (D-047), so "off" cannot be expressed by an ordinary manifest: the tombstone or erase writer above amends that rule.

### Expected tests
- Integration (PostgreSQL): deletion and import with recognition off.
