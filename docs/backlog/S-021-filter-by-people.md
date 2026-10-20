## S-021 — Filter by people
**Epic:** People
**Depends on:** S-020, S-013, S-026
**Priority:** High
**Size:** M

### Context
"Photos of me with Ana and Pedro together" ("Fotos minhas com a Ana e o Pedro juntos") is the most common question.

### Scope
- DSL: `people: { "all": ["Ana", "Pedro"], "includeMe": true }` (all the people in the same photo; `includeMe` resolves the current user's "This is me").
- People selector in the filter bar; the photo detail lists the people with a link to the filter.
- Questions (S-025): the translator's context gains the people's names and "me", and its prompt examples cover people ("photos of me with Ana and Pedro").
- Counting (S-026): `groupBy: "person"` ("Who did I take the most photos with in 2025?").

### Out of scope

### Acceptance criteria
- [ ] Given photos with Ana, Pedro and both, when I filter `all=[Ana,Pedro]`, then only those with both come back.
- [ ] Given `includeMe` for a user with "This is me", then that filter is applied.
- [ ] Given an unknown name, then the API returns 400 with the missing name.
- [ ] Given photos with several people in 2025, when I group by person with `top=3`, then the 3 with the most photos are returned, in order.
- [ ] Given existing people, when the question prompt is built, then it includes their names.

### Technical notes
- Extends `search.v1.json` and the executor.

### Expected tests
- Integration (PostgreSQL): "all" semantics, `includeMe`, errors.
