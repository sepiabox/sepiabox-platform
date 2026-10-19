## S-007 — Upload originals, derivatives and manifests to storage
**Epic:** Local ingest
**Depends on:** S-003, S-004
**Priority:** High
**Size:** M

### Context
After `process`, the PC sends everything to Blob Storage. The manifest goes last, because its arrival triggers the import (S-033). Uploading ~1.1 TB takes days, so it must resume.

### Scope
- `sepiabox-ingest upload <workdir> --destination local|azure [--account <name>]`.
- `local`: connection string `UseDevelopmentStorage=true` or the `SEPIABOX_BLOB_CONNECTION` variable. `azure`: `https://<account>.blob.core.windows.net` with `DefaultAzureCredential` (login via `az login`).
- Containers: `originals` (blob `<sha256>`, Cold tier only for the azure destination), `derivatives` (`<sha256>/<name>`), `manifests` (`<sha256>.json`, uploaded only after the original and the derivatives).
- SQLite state: what has already been uploaded; existing blobs with the same size are skipped.
- Before uploading an original, confirm the SHA-256 still matches; if not, report and skip.
- 4 parallel uploads, 8 MB blocks, progress in bytes.

### Out of scope
- Catalog import (S-008).

### Acceptance criteria
- [ ] Given a processed workdir and Azurite, when I run `upload --destination local`, then the three containers have the expected blobs.
- [ ] Given the same command again, when it runs, then nothing is re-uploaded.
- [ ] Given a cancellation midway, when I run it again, then the rest is uploaded and nothing is duplicated.
- [ ] Given an original changed after `process`, when I run `upload`, then it is not uploaded and appears as a problem.
- [ ] Given an asset, when it is uploaded, then its manifest is only uploaded after the original and the derivatives (checked through the order recorded in the state).

### Technical notes
- Packages: `Azure.Storage.Blobs`, `Azure.Identity`; tests with `Testcontainers.Azurite`.
- Azurite may not support the Cold tier: apply `AccessTier.Cold` only when the destination is azure.
- Decisions: D-004, D-005.

### Expected tests
- Integration (Azurite): upload, resume, hash check, order.
