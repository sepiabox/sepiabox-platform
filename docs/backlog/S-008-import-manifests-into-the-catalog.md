## S-008 — Import manifests into the catalog
**Epic:** Cloud catalog
**Depends on:** S-003, S-007
**Priority:** High
**Size:** M

### Context
The PostgreSQL catalog is the source of truth for search. The worker reads manifests from Blob Storage and writes them idempotently. In this story it runs on demand (`--once`); automatic triggering comes in S-033.

### Scope
- `src/Sepiabox.Catalog`: `CatalogDbContext` (EF Core + Npgsql + NetTopologySuite) and an initial migration with `assets` (id = sha256, kind, size_bytes, content_type, captured_at, location `geography(Point,4326)`, camera_make, camera_model, width, height, duration_seconds, imported_at), `asset_sources` (asset_id, type, name, path, modified_at; unique per asset+type+name+path), `derivatives` (asset_id, name, blob_path, width, height, content_type), `manifest_imports` (blob_name, etag, imported_at).
- `src/Sepiabox.Worker` (`sepiabox-worker`): `migrate` and `import --once` (imports manifests that are new or have a different etag).
- Validation against `manifest.v1.json`; an invalid manifest is logged and skipped.
- Shared library: no owner column (D-018).
- AppHost: adds the worker with references to the database and blob storage.

### Out of scope
- Event triggering (S-033), places (S-012), embeddings (S-015).

### Acceptance criteria
- [ ] Given a manifest in Azurite, when I run `import --once`, then the asset, its sources and its derivatives exist in PostgreSQL.
- [ ] Given the same manifest imported twice, when the second import finishes, then there are no duplicate rows.
- [ ] Given a manifest for the same asset from another source, when it is imported, then a source is added to the existing asset.
- [ ] Given an invalid manifest among valid ones, when I run `import --once`, then the valid ones are imported and the invalid one is reported.
- [ ] Given an empty database, when I run `migrate`, then all migrations apply.

### Technical notes
- Test PostgreSQL with Testcontainers built from `infra/postgres/Dockerfile` (`ImageFromDockerfileBuilder`).
- Decisions: D-003, D-013, D-014, D-018.

### Expected tests
- Integration (PostgreSQL + Azurite): import, idempotency, multiple sources, invalid manifests, migration.
