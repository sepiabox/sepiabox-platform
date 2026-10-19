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

### Out of scope
- Per-user encryption.

### Acceptance criteria
- [ ] Given recognition turned off, then `faces` and `people` are empty.
- [ ] Given recognition turned off, when I import a manifest with faces, then no face is stored.
- [ ] Given "Apagar dados de rosto" on a person, then their faces disappear and the photos remain.

### Technical notes
- Table `settings` (key/value).

### Expected tests
- Integration (PostgreSQL): deletion and import with recognition off.
