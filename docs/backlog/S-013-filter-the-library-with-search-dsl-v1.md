## S-013 — Filter the library with search DSL v1
**Epic:** Search
**Depends on:** S-012
**Priority:** High
**Size:** M

### Context
All search goes through a single JSON DSL (D-020), used by the filter UI and, later, by the question translator. This story creates the DSL with time, kind, place and source filters.

### Scope
- C# records and `docs/schemas/search.v1.json`:
```json
{
  "filters": {
    "kinds": ["photo", "video"],
    "dateFrom": "2019-01-01", "dateTo": "2019-12-31",
    "years": [2019], "months": [12],
    "onThisDay": { "yearsAgo": 5 },
    "places": { "cities": ["Lisbon"], "countries": ["PT"] },
    "sources": ["HD da Ana"],
    "undatedOnly": false
  },
  "sort": "capturedAtDesc",
  "limit": 200
}
```
- `SearchService.Execute(SearchQuery)` (EF Core), `POST /api/search` (authenticated).
- Filter bar on `/` (year, month, kind, city, source) and an "On this day" shortcut ("Neste dia" in pt-BR); filters in the URL (`?q=<json base64url>`).
- Invalid DSL → 400 with a message.

### Out of scope
- People, personal places, visual, occasions (later stories extend the DSL).

### Acceptance criteria
- [x] Given assets from Lisbon and Porto across several months, when I filter `cities=[Lisbon], months=[12]`, then only the Lisbon ones from December come back.
- [x] Given a clock fixed at 2026-10-19 and `onThisDay.yearsAgo=5`, then only assets from 2021-10-19 come back.
- [x] Given several filters, then they are combined with AND.
- [x] Given a DSL with an unknown field, then the API returns 400.
- [x] Given filters chosen in the UI, when I copy the URL and open it, then the same filters are applied.

### Technical notes
- `Sepiabox.Catalog/Search/SearchQuery.cs`, `SearchService.cs`; `TimeProvider` for the clock.
- Portability (D-051): `SearchService` builds its filters with plain EF Core (no provider-specific SQL); later filters that need PostGIS (S-014) or pgvector (S-016) plug in provider-specific classes behind it.
- Decisions: D-020, D-051.

### Expected tests
- Unit: DSL validation, URL round trip. Integration (PostgreSQL): each filter and combinations.
