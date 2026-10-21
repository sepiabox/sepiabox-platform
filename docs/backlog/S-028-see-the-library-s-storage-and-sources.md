## S-028 — See the library's storage and sources
**Epic:** Organization
**Depends on:** S-026
**Priority:** Low
**Size:** S

### Context
"How much space do the 2019 videos take?" ("Quanto espaço ocupam os vídeos de 2019?") and "Which photos exist in only one source?" ("Que fotos só existem em uma fonte?") help decide what to keep.

### Scope
- DSL: `aggregate.sum: "bytes"`; filter `sourceCount: { "eq": 1 }` or `{ "gte": 2 }`.
- Page `/storage`: totals by kind and year (count and size), assets in only one source, assets in several sources (with names), the 20 largest videos.

### Out of scope
- Automatic cleanup recommendations.

### Acceptance criteria
- [x] Given seeded assets, when the page is loaded, then the totals by kind and year are correct.
- [x] Given assets with 1 and 2 sources, when filtering with `sourceCount.eq=1`, then only the former are returned.
- [x] Given `aggregate.sum=bytes` with `kinds=[video]` and `years=[2019]`, when the query runs, then it returns the correct sum.

### Technical notes
- Reuses the aggregation executor from S-026.

### Expected tests
- Integration (PostgreSQL): totals and filters. bUnit: page.
