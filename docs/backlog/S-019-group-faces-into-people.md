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
- Page `/people`: people by number of faces, face crop from the thumbnail (CSS with the relative box), "Unnamed" ("Sem nome" in pt-BR) when applicable.

### Out of scope
- Renaming and merging (S-020).

### Acceptance criteria
- [ ] Given two faces with nearly identical embeddings, then they end up in the same person.
- [ ] Given two distant faces, then they end up in different people.
- [ ] Given the same manifest imported twice, then faces are not duplicated.
- [ ] Given the `/people` page, then people appear in descending order of faces.

### Technical notes
- `Sepiabox.Catalog/People/FaceAssigner.cs`. Threshold in configuration.
- `facesProcessed: false` in a manifest means "not looked for" (D-047), never "no faces": importing it never deletes the asset's stored face rows. `facesProcessed: true` replaces them. Store `facesVersion` (missing reads as 1) per asset; faces of a newer version replace older rows, and a person's centroid never mixes embeddings of two versions.
- When a manifest's faces replace stored ones, match each incoming face to a stored face of the same asset by box IoU ≥ 0.5 and keep that row's `person_id` and `assignment`, so a re-import (or a merge that picked the other computer's faces, D-047) does not undo the family's curation.
- A face whose embedding passes the schema but is not finite or has no length is skipped and listed once in the import's warnings, as embeddings are (D-043).
- Boxes are relative to the upright derivatives: map them with a derivative's width and height (`thumb-256`, `preview-1280`, `poster`), never the manifest's `width`/`height`, which describe the original as stored.
- Decisions: D-016, D-018.

### Expected tests
- Unit: assignment with synthetic vectors. Integration: idempotent import. bUnit: ordering.
