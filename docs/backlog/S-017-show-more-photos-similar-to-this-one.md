## S-017 — Show more photos similar to this one
**Epic:** Search
**Depends on:** S-015, S-013
**Priority:** Medium
**Size:** S

### Context
"Mais fotos parecidas com esta" ("More photos similar to this one") uses the photo's own stored embedding, without recomputing anything.

### Scope
- DSL: `similarTo: { "assetId": "<sha256>", "limit": 50 }`; excludes the asset itself; combinable with filters.
- "Parecidas" ("Similar") button on the photo detail → results grid.

### Out of scope
- Similarity by photo region (S-041).

### Acceptance criteria
- [ ] Given synthetic embeddings, then results come in order of similarity and do not include the photo itself.
- [ ] Given an asset without an embedding, then the UI shows "Esta foto ainda não foi analisada" ("This photo has not been analysed yet").
- [ ] Given `similarTo` with `years=[2020]`, then only assets from 2020 come back.

### Technical notes
- pgvector `<=>` operator.

### Expected tests
- Integration (PostgreSQL): order, exclusion, filters. bUnit: message when there is no embedding.
