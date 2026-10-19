## S-014 — Save personal places and filter by them
**Epic:** Search
**Depends on:** S-013
**Priority:** Medium
**Size:** S

### Context
"A casa da praia ao longo dos anos" ("The beach house over the years") and "fotos a mais de 100 km de casa" ("photos more than 100 km from home") depend on places only the family knows.

### Scope
- Table `saved_places` (id, name unique case-insensitively, location, radius_m).
- Page `/lugares`: create from a photo's location ("Guardar este lugar", "Save this place") or from lat/lon, edit the radius, delete.
- DSL: `savedPlaces: ["Casa da praia"]`, `distanceFrom: { "place": "Casa", "minKm": 100 }`.
- "Over the years" view for a place (grouped by year).

### Out of scope
- Automatic detection of "home".

### Acceptance criteria
- [ ] Given a place with a 200 m radius, then the filter returns only assets inside the radius.
- [ ] Given `distanceFrom` Casa with `minKm=100`, then only assets more than 100 km away come back.
- [ ] Given a deleted place, then the assets remain intact.
- [ ] Given two places with the same name in different letter case, then the second is rejected.

### Technical notes
- `ST_DWithin` on `geography`. Extends `search.v1.json`.

### Expected tests
- Integration (PostgreSQL): radius, minimum distance, uniqueness. bUnit: form.
