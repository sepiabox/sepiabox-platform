## S-040 — Export all data
**Epic:** Privacy
**Depends on:** S-020, S-010, S-051, S-063
**Priority:** Medium
**Size:** S

### Context
"Your photos are yours": at any time, the family can take everything with them, in open formats.

### Scope
- `/settings/export`: requests an export; the worker generates `exports/<date>.zip` with `catalog.json` (assets, sources, people with names, occasions, personal places), `originals.txt` (one line per original with a 24 h SAS) and `README.txt`.
- Face data (embeddings) only with the "Include face data" option.
- 1 h SAS download link.
- Index-only assets (S-063, D-088): `originals.txt` lists only the originals Sepiabox stores; `catalog.json` carries each asset's `originalStored` and each source's `url`, so the export says where every index-only original lives.

### Out of scope
- Physically copying the originals into the zip.

### Acceptance criteria
- [ ] Given a seeded library, then `catalog.json` contains all assets.
- [ ] Given the default export, then it contains no face embeddings.
- [ ] Given `originals.txt`, then each SAS expires in ≤ 24 h.
- [ ] Given an index-only asset, when the export runs, then `originals.txt` has no line for it and `catalog.json` lists its sources with their `url`.

### Technical notes
- `System.IO.Compression.ZipArchive` streaming to the blob through `IBlobStore`'s staged upload, and the links from its signed read URL (D-051, S-050), not the Blob SDK directly.

### Expected tests
- Integration (Azurite + PostgreSQL): zip contents and expirations.
