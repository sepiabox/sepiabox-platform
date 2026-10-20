## S-012 — Name places with GeoNames
**Epic:** Cloud catalog
**Depends on:** S-008
**Priority:** High
**Size:** M

### Context
"Photos of Lisbon" ("Fotos de Lisboa") needs place names. GeoNames is used offline instead of a pay-per-query service (D-009).

### Scope
- Table `geonames` (geoname_id, name, ascii_name, country_code, admin1_name, population, location geography) with a GiST index.
- `sepiabox-worker geonames load <cities500.zip|txt> --admin1 <admin1CodesASCII.txt> --countries <countryInfo.txt>`: idempotent load (source: `https://download.geonames.org/export/dump/`).
- On import: nearest city within 50 km → `place_city`, `place_region`, `place_country` on the asset.
- `sepiabox-worker places refresh`: resolves assets without a place.
- Photo detail shows "Matosinhos, Porto, Portugal".
- CC BY 4.0 attribution in the README.

### Out of scope
- Personal places (S-014).

### Acceptance criteria
- [ ] Given a test file with 5 cities, when I load it twice, then the table has 5 rows.
- [ ] Given an asset at 41.18, −8.69, when it is imported, then the place is "Matosinhos".
- [ ] Given an asset 300 km from the nearest city, then it has no place.
- [ ] Given `places refresh`, then older assets without a place get resolved.

### Technical notes
- Query: `ORDER BY location <-> @point LIMIT 1` with `ST_DWithin(location, @point, 50000)`.
- Place names are stored as GeoNames writes them (`name`, often the English name for large cities, "Lisbon"); they are data, not translated UI text (D-050). Alternate names are out of scope: the question translator (S-025) maps "Lisboa" to the stored name.
- Small TSV fixture at `tests/.../TestData/geonames-sample.txt`.
- Decisions: D-009, D-050.

### Expected tests
- Integration (PostgreSQL): load, resolution, radius, refresh.
