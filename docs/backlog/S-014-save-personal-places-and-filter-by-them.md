## S-014 — Save personal places and filter by them
**Epic:** Search
**Depends on:** S-013, S-025
**Priority:** Medium
**Size:** M

### Context
"The beach house over the years" ("A casa da praia ao longo dos anos") and "photos more than 100 km from home" ("fotos a mais de 100 km de casa") depend on places only the family knows.

### Scope
- Table `saved_places` (id, name unique case-insensitively, location, radius_m, is_home). At most one place is the home: marking another moves the mark. Names are what the family types; "home" is a flag, not a name, so it works in any language (D-050).
- Page `/places`: create from a photo's location ("Save this place"; "Salvar este lugar" in pt-BR) or from lat/lon, edit the radius, delete.
- DSL: `savedPlaces: ["Beach house"]`, `distanceFrom: { "place": "Beach house", "minKm": 100 }` or `distanceFrom: { "home": true, "minKm": 100 }`.
- "Over the years" view for a place (grouped by year).
- Questions (S-025): the translator's context gains the saved place names and the home place, with prompt examples ("photos at the beach house", "more than 100 km from home").

### Out of scope
- Automatic detection of "home".

### Acceptance criteria
- [ ] Given a place with a 200 m radius, then the filter returns only assets inside the radius.
- [ ] Given `distanceFrom` the home place with `minKm=100`, then only assets more than 100 km away come back.
- [ ] Given a place marked as home, when another place is marked as home, then only the second keeps the mark.
- [ ] Given a deleted place, then the assets remain intact.
- [ ] Given two places with the same name in different letter case, then the second is rejected.

### Technical notes
- `ST_DWithin` on `geography`. Extends `search.v1.json`.

### Expected tests
- Integration (PostgreSQL): radius, minimum distance, uniqueness. bUnit: form.
