## S-040 — Export all data
**Epic:** Privacy
**Depends on:** S-020, S-010
**Priority:** Medium
**Size:** S

### Context
"Your photos are yours": at any time, the family can take everything with them, in open formats.

### Scope
- `/definicoes/exportar`: requests an export; the worker generates `exports/<date>.zip` with `catalog.json` (assets, sources, people with names, occasions, personal places), `originals.txt` (one line per original with a 24 h SAS) and `README.txt`.
- Face data (embeddings) only with the "incluir dados de rosto" ("include face data") option.
- 1 h SAS download link.

### Out of scope
- Physically copying the originals into the zip.

### Acceptance criteria
- [ ] Given a seeded library, then `catalog.json` contains all assets.
- [ ] Given the default export, then it contains no face embeddings.
- [ ] Given `originals.txt`, then each SAS expires in ≤ 24 h.

### Technical notes
- `System.IO.Compression.ZipArchive` streaming to the blob.

### Expected tests
- Integration (Azurite + PostgreSQL): zip contents and expirations.
