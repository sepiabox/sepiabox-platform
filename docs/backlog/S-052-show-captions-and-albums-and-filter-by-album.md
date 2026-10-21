## S-052 — Show captions and albums and filter by album
**Epic:** Import exports
**Depends on:** S-045, S-013, S-044
**Priority:** Medium
**Size:** S

### Context
Manifests from exports carry captions and album names (S-045). The family should see them on the photo and find an album's photos again.

### Scope
- Catalog: `assets.caption` (nullable text) and table `asset_albums` (asset_id, name; unique per asset), filled by the import from the manifest; a manifest without `albums` leaves the stored albums as they are (sources and albums only grow, as D-032 does for sources).
- Photo detail: the caption and the album names, each album linking to the album filter; labels from the resources in English and `pt-BR` (D-050), the caption and album names shown as the family wrote them.
- DSL: `albums: ["Summer 2019"]` (assets in any of the albums, names compared case-insensitively); an album selector in the filter bar listing existing albums.

### Out of scope
- Editing captions or albums in the app.
- Captions in natural-language questions (S-025 may add `albums` to its context later).

### Acceptance criteria
- [ ] Given a manifest with a caption and two albums, when it is imported, then the asset has the caption and both albums; importing it again adds no duplicate.
- [ ] Given a second manifest of the same asset with a third album, when it is imported, then the asset has the three albums.
- [ ] Given assets in albums "Summer 2019" and "Wedding", when I filter `albums=[summer 2019]`, then only the first album's assets come back.
- [ ] Given the photo detail in `en` and `pt-BR`, then the caption and albums are shown with localized labels.

### Technical notes
- Runs after S-040 (full export) and S-057 (deleting a source's assets), D-090: the export's `catalog.json` gains the albums and captions here, and deleting a source also removes its album memberships.
- New migration (D-033); extends `search.v1.json` and the executor (D-020).
- Decisions: D-020, D-032, D-033, D-050, D-053.

### Expected tests
- Integration (PostgreSQL): import, idempotence, album filter. bUnit: detail page in both cultures.
