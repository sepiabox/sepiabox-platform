## S-023 — Group photos into occasions
**Epic:** Occasions
**Depends on:** S-012
**Priority:** Medium
**Size:** M

### Context
"Quantos casamentos fomos" ("How many weddings did we go to") counts occasions, not photos. An occasion is a sequence of photos close together in time and space (D-019).

### Scope
- Table `events` (id, starts_at, ends_at, centroid, place_label, asset_count, nullable type, title) and `event_assets`.
- `sepiabox-worker events rebuild`: sorts dated assets; starts a new event when the gap is > 6 h or the distance to the previous geotagged asset is > 50 km; undated assets are left out.
- Title: "<place> · <dates>" (e.g. "Lisboa · 14–16 dez 2024").
- Page `/ocasioes` (cards with 3 thumbnails, dates, place, count) and a detail page; DSL `eventIds`.

### Out of scope
- Occasion type (S-024).

### Acceptance criteria
- [ ] Given photos 7 h apart, when events are rebuilt, then they end up in different occasions.
- [ ] Given photos 1 h and 60 km apart, when events are rebuilt, then they end up in different occasions.
- [ ] Given `rebuild` run twice, when the results are compared, then they are identical.
- [ ] Given undated assets, when events are rebuilt, then they belong to no occasion.
- [ ] Given an occasion in Lisbon from 14 to 16/12/2024, when its title is generated, then the title is "Lisboa · 14–16 dez 2024".

### Technical notes
- `Sepiabox.Catalog/Events/EventBuilder.cs` (pure, testable logic), with persistence kept separate.
- Decisions: D-019.

### Expected tests
- Unit: split rules and titles. Integration: idempotent rebuild.
