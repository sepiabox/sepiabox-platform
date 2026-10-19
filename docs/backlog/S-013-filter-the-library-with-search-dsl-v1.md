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
    "places": { "cities": ["Lisboa"], "countries": ["PT"] },
    "sources": ["HD da Ana"],
    "undatedOnly": false
  },
  "sort": "capturedAtDesc",
  "limit": 200
}
```
- `SearchService.Execute(SearchQuery)` (EF Core), `POST /api/search` (authenticated).
- Filter bar on `/` (year, month, kind, city, source) and a "Neste dia" ("On this day") shortcut; filters in the URL (`?q=<json base64url>`).
- Invalid DSL → 400 with a message.

### Out of scope
- People, personal places, visual, occasions (later stories extend the DSL).

### Acceptance criteria
- [ ] Given assets from Lisbon and Porto across several months, when I filter `cities=[Lisboa], months=[12]`, then only the Lisbon ones from December come back.
- [ ] Given a clock fixed at 2026-10-19 and `onThisDay.yearsAgo=5`, then only assets from 2021-10-19 come back.
- [ ] Given several filters, then they are combined with AND.
- [ ] Given a DSL with an unknown field, then the API returns 400.
- [ ] Given filters chosen in the UI, when I copy the URL and open it, then the same filters are applied.

### Technical notes
- `Sepiabox.Catalog/Search/SearchQuery.cs`, `SearchService.cs`; `TimeProvider` for the clock.
- Decisions: D-020.

### Expected tests
- Unit: DSL validation, URL round trip. Integration (PostgreSQL): each filter and combinations.
