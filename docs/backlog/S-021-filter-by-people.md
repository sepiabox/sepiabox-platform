## S-021 — Filter by people
**Epic:** People
**Depends on:** S-020, S-013
**Priority:** High
**Size:** S

### Context
"Fotos minhas com a Ana e o Pedro juntos" ("Photos of me with Ana and Pedro together") is the most common question.

### Scope
- DSL: `people: { "all": ["Ana", "Pedro"], "includeMe": true }` (all the people in the same photo; `includeMe` resolves the current user's "Sou eu").
- People selector in the filter bar; the photo detail lists the people with a link to the filter.

### Out of scope
- Aggregations by person (S-026).

### Acceptance criteria
- [ ] Given photos with Ana, Pedro and both, when I filter `all=[Ana,Pedro]`, then only those with both come back.
- [ ] Given `includeMe` for a user with "Sou eu", then that filter is applied.
- [ ] Given an unknown name, then the API returns 400 with the missing name.

### Technical notes
- Extends `search.v1.json` and the executor.

### Expected tests
- Integration (PostgreSQL): "all" semantics, `includeMe`, errors.
