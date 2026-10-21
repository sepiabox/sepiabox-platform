## S-022 — Turn off face recognition and delete face data
**Epic:** People
**Depends on:** S-019
**Priority:** Medium
**Size:** S

### Context
Faces are biometric data (LGPD/GDPR). The family must be able to turn it off and delete it.

### Scope
- `/settings`: library face recognition toggle (confirmation on the screen itself).
- Turning it off deletes all rows in `faces` and `people`; with recognition off, import ignores faces in manifests.
- Per person: "Delete face data" removes the person and their faces (the photos stay).
- Turning it off also removes faces from the stored manifest blobs, or the next import (or turning it back on) would bring them back. The erase goes through `ManifestStore.WriteMergedAsync` (D-036), never a direct upload, and must win the merge: an explicit erase rule such as a `facesErasedAt` tombstone (newer than the faces wins, merged like `modifiedAt`) or a dedicated erase writer that sets `facesProcessed: false` and `faces: []` and is never undone by an older `facesProcessed: true` manifest from a work folder (D-047). A tombstone alone cannot tell faces found before the erase from faces found after recognition was turned back on: give faces a detection time (an optional `facesDetectedAt`, merged with the faces) so faces detected after the tombstone win over it and older ones lose, whichever computer uploads last.
- Say on the screen and in `docs/operations.md` how to clear faces from a `process` work folder too: delete the work folder, or run `process` into a fresh one without `--faces`, before any further `upload`; otherwise uploading it brings the faces back.

### Out of scope
- Per-user encryption.

### Acceptance criteria
- [x] Given recognition turned off, then `faces` and `people` are empty. *(`FaceRecognitionTests`: faces, people and face versions gone, the photos kept, the deletion recorded with who; over HTTP from `/settings` in `FaceSettingsCatalogTests`.)*
- [x] Given recognition turned off, when I import a manifest with faces, then no face is stored. *(`FaceRecognitionTests`; once it is back on, only faces found after the erase are stored. The stored manifests: `FacesErasureMergeTests`, `ManifestStoreTests` and the sweep in `FaceRecognitionTests`.)*
- [x] Given "Delete face data" on a person, then their faces disappear and the photos remain. *(`FaceRecognitionTests`, including a re-import that does not bring them back; the confirmation in `FaceSettingsPageTests` and `FaceSettingsPostTests`.)*

### Technical notes
- Table `settings` (key/value). The worker's face opt-in for files that arrive in the cloud (S-036) reads this setting; it is off by default (D-047).
- `ManifestMerge` today keeps faces from the side with `facesProcessed: true` (D-047), so "off" cannot be expressed by an ordinary manifest: the tombstone or erase writer above amends that rule.
- Decisions: D-036, D-047, D-077, D-081.

### Status
- `done` (2026-10-21): implemented and tested (D-081). The `sepiabox-faces` job is new infrastructure: the next deploy creates it, and the owner's check is in `docs/operations.md`.

### Expected tests
- Integration (PostgreSQL): deletion and import with recognition off.
