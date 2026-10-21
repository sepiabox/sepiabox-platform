## S-017 — Show more photos similar to this one
**Epic:** Search
**Depends on:** S-015, S-013
**Priority:** Medium
**Size:** S

### Context
"More photos similar to this one" ("Mais fotos parecidas com esta") uses the photo's own stored embedding, without recomputing anything.

### Scope
- DSL: `similarTo: { "assetId": "<sha256>", "limit": 50 }`; excludes the asset itself; combinable with filters.
- "Similar" button ("Parecidas" in pt-BR) on the photo detail → results grid.

### Out of scope
- Similarity by photo region (S-041).

### Acceptance criteria
- [x] Given synthetic embeddings, then results come in order of similarity and do not include the photo itself. (`SimilarPhotosTests`, ranked through the index walk and exactly.)
- [x] Given an asset without an embedding, then the UI shows "This photo has not been analysed yet" ("Esta foto ainda não foi analisada" in pt-BR). (`PhotoDetailTests`, `TimelineFilterTests`, and the real page in `SimilarPhotosTests`.)
- [x] Given `similarTo` with `years=[2020]`, then only assets from 2020 come back. (`SimilarPhotosTests`, the service, the page and `POST /api/search`.)

### Technical notes
- pgvector `<=>` operator.

### Status
- `doing` (2026-10-21): implemented and tested (D-086). The translator's output format and prompt are unchanged, so no golden recording is needed.

### Expected tests
- Integration (PostgreSQL): order, exclusion, filters. bUnit: message when there is no embedding.
