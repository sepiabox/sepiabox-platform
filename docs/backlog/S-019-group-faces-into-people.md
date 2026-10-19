## S-019 — Group faces into people
**Epic:** People
**Depends on:** S-018, S-010
**Priority:** High
**Size:** M

### Context
Similar faces become an unnamed person; the family only gives names (curation).

### Scope
- Tables `people` (id, name nullable, user_email nullable, face_count, centroid `vector(128)`) and `faces` (id, asset_id, box, score, embedding `vector(128)`, person_id, assignment `auto|manual|excluded`).
- On import: cosine with the nearest centroid ≥ 0.40 → assign; otherwise a new person. Recompute centroid and count.
- Page `/pessoas`: people by number of faces, face crop from the thumbnail (CSS with the relative box), "Sem nome" ("Unnamed") when applicable.

### Out of scope
- Renaming and merging (S-020).

### Acceptance criteria
- [ ] Given two faces with nearly identical embeddings, then they end up in the same person.
- [ ] Given two distant faces, then they end up in different people.
- [ ] Given the same manifest imported twice, then faces are not duplicated.
- [ ] Given the `/pessoas` page, then people appear in descending order of faces.

### Technical notes
- `Sepiabox.Catalog/People/FaceAssigner.cs`. Threshold in configuration.
- Decisions: D-016, D-018.

### Expected tests
- Unit: assignment with synthetic vectors. Integration: idempotent import. bUnit: ordering.
