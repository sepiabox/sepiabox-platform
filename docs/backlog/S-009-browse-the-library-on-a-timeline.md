## S-009 — Browse the library on a timeline
**Epic:** Web app
**Depends on:** S-008
**Priority:** High
**Size:** M

### Context
The first time the family sees the drive's photos in the browser. The web shows thumbnails through short-lived SAS URLs, never exposing storage.

### Scope
- Page `/`: thumbnail grid grouped by month (most recent first), assets without a date in a "Sem data" ("No date") group at the end, pages of 200.
- Page `/fotos/{id}`: 1280 preview, date, camera, dimensions, sources, link to the original.
- `SasUrlFactory`: read SAS for `thumb-256`/`preview-1280` valid for 15 min; original 5 min. Azure: user delegation SAS (managed identity). Azurite: account key SAS.
- Desktop layout (minimum width ~1024 px), colours and typefaces from `docs/architecture.md` (Visual identity), UI text in Portuguese.

### Out of scope
- Login (S-010), filters (S-013).

### Acceptance criteria
- [ ] Given 3 assets in different months, when I open `/`, then I see 3 month groups in descending order.
- [ ] Given an asset without a date, when I open `/`, then it appears under "Sem data" at the end.
- [ ] Given a thumbnail, when the browser requests it (`/fotos/{id}/thumb-256`), then the web redirects to a read-only SAS that expires in ≤ 15 min, and the page itself contains no SAS URLs (D-039).
- [ ] Given a non-existent `/fotos/{id}`, when I request it, then the response is 404.

### Technical notes
- `Sepiabox.Web/Components/Pages/Timeline.razor`, `PhotoDetail.razor`, `Services/SasUrlFactory.cs`, `Services/LibraryQueries.cs`.
- Component tests with bUnit; queries with a test PostgreSQL.
- Decisions: D-004, D-018.

### Expected tests
- Unit: `SasUrlFactory` (expiry, permissions). bUnit: grouping and "Sem data". Integration: 404 and paginated query.
