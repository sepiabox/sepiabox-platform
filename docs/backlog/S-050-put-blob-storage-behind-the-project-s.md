## S-050 — Put blob storage behind the project's own interface
**Epic:** Foundation
**Depends on:** S-007, S-008, S-009, S-033
**Priority:** High
**Size:** M

### Context
Azure-specific services sit behind the project's own small interfaces, so any part can move (D-051). Today the Blob SDK is used directly in four places: Pipeline's `ManifestStore`, the worker's `ManifestImporter`, the web's `SasUrlFactory` and the ingest `upload`. This story, after the MVP, moves the first three behind `IBlobStore`, and moves S-033's `IBlobEventQueue` from the worker into `Sepiabox.Storage`; `upload` follows in S-051.

### Scope
- New library `Sepiabox.Storage` (added to `Sepiabox.slnx`, with its test project `Sepiabox.Storage.Tests`): `IBlobStore` with what the code uses today: read a blob with its version tag (`null` when missing), list names by container and prefix, write (unconditionally, only if new, or only if the version still matches; a lost condition is a typed result, not an exception), delete, and a signed read URL for one blob (lifetime, optional download file name and content type).
- `BlobLayout` moves from `Sepiabox.Pipeline` to `Sepiabox.Storage`; the web references `Sepiabox.Storage` and drops `StorageContainers` and the test that kept the copies equal (amends D-038).
- `AzureBlobStore` (namespace `Sepiabox.Storage.Azure`) implements it with `BlobServiceClient`: ETags as version tags, `If-Match`/`If-None-Match: *` for conditional writes, 409/412 as a lost condition; the signing of `SasUrlFactory` (D-039: account key with Azurite, user delegation key reused for an hour otherwise, HTTPS only when storage is HTTPS) moves into it unchanged.
- `InMemoryBlobStore` (in `Sepiabox.Storage`, for tests and local experiments) with the same conditional-write semantics.
- `ManifestStore.WriteMergedAsync` (D-036), the worker's `import --once` and the web's media endpoints use `IBlobStore`; `ISasUrlFactory` keeps its derivative and original rules (windows, cache headers, D-039) on top of `IBlobStore`'s signed URL.
- Registration in each `Program.cs` (the composition root): `IBlobStore` → `AzureBlobStore` from the Aspire `BlobServiceClient`.

### Out of scope
- `sepiabox-ingest upload` (S-051).
- Changing the queue: `IBlobEventQueue` and its Azure implementation only move from the worker to `Sepiabox.Storage`.
- Any behaviour change: the D-036 merge, the D-039 links and the import results stay exactly as they are.

### Acceptance criteria
- [ ] Given `InMemoryBlobStore` and `AzureBlobStore` (Azurite), when the same contract tests run against both, then they pass: write if new fails when the blob exists, write if the version matches fails after another write, a missing blob reads as `null`, list by prefix returns only matching names.
- [ ] Given the existing `ManifestStore` tests (concurrent writers, retries, the skip when nothing changed), when they run on `InMemoryBlobStore` and on Azurite, then they pass unchanged in intent.
- [ ] Given the existing worker import and web media tests, when they run, then they pass with `IBlobStore` registered.
- [ ] Given the projects `Sepiabox.Pipeline`, `Sepiabox.Worker` and `Sepiabox.Web` outside their `Program.cs`, when their source is checked by a test, then no file uses an `Azure.Storage` namespace.
- [ ] Given the web project, when its references are inspected, then it still does not reference `Sepiabox.Pipeline` (D-038).

### Technical notes
- Keep `IBlobStore` minimal: only operations a current caller needs; S-051 adds the staged upload and access tier.
- `Sepiabox.Storage` references only `Azure.Storage.Blobs` (and `Azure.Identity` if the delegation key needs it), no media libraries, so the web image stays small.
- Decisions: D-030, D-036, D-038, D-039, D-051.

### Expected tests
- Contract tests for `IBlobStore` run against both implementations (Azurite ones marked Integration).
- Existing Pipeline, Worker and Web tests moved to `IBlobStore`; a source check for `Azure.Storage` namespaces outside the allowed files.
