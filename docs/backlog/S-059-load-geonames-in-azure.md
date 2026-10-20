## S-059 — Load GeoNames in Azure
**Epic:** Cloud catalog
**Depends on:** S-012, S-058
**Priority:** High
**Size:** S

### Context
S-012 loads GeoNames and resolves places with worker commands. The cloud catalog needs the same data, with no PC involved.

### Scope
- A manual-trigger Container Apps job `sepiabox-geonames` (worker image, the worker identity of S-058) that downloads `cities500.zip`, `admin1CodesASCII.txt` and `countryInfo.txt` from GeoNames, runs `geonames load` and then `places refresh`.
- `docs/operations.md`: start it with `az containerapp job start --name sepiabox-geonames --resource-group rg-sepiabox`, once after the first deploy, and again only if places are missing after a large upload of old photos.

### Out of scope
- A schedule (GeoNames changes slowly; a manual run is enough).

### Acceptance criteria
- [ ] Given the generated Bicep, then it has the `sepiabox-geonames` job with a manual trigger, the worker image, the catalog connection from Key Vault and no schedule.
- [ ] Given a real deploy (manual, owner acceptance in `docs/operations.md`), when the job has run and a photo taken in Lisbon is imported, then its detail page shows "Lisbon".

### Technical notes
- Download safeguards (D-066, from the S-012 review): download only over HTTPS from `download.geonames.org`; log each file's SHA-256, size and date (a pinned hash is not possible, GeoNames republishes daily); refuse the load when the city count is below 150,000 or more than about 10% below the previous load's; on any failure keep the previous `geonames` table (`geonames load` already replaces it in one transaction).
- Decisions: D-009, D-034, D-066.

### Expected tests
- Infrastructure generation test.
