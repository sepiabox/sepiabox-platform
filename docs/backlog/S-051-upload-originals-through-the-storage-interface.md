## S-051 — Upload originals through the storage interface
**Epic:** Foundation
**Depends on:** S-050
**Priority:** Medium
**Size:** M

### Context
After S-050, `sepiabox-ingest upload` is the last code that uses the Blob SDK directly (D-051). Its upload of originals reads each file once: the SHA-256 is computed while 8 MB blocks are staged and the block list is committed only if the hash matches the manifest (D-030); originals go to the Cold tier (D-004).

### Scope
- `IBlobStore` gains a staged upload: start an upload for a blob, append blocks, and commit with an optional storage class (`Cold` for originals, D-004) and content type; an upload never committed leaves no blob. `AzureBlobStore` maps it to `StageBlock`/`CommitBlockList` with the access tier; `InMemoryBlobStore` keeps uncommitted blocks apart.
- `UploadCommand` uses `IBlobStore` for originals (staged), derivatives and manifests (through `ManifestStore`, D-036); the creation of the Azure client from `--to azure|local` moves to the ingest composition root.
- Container creation (`CreateIfNotExists`) becomes an `IBlobStore` operation used by `upload` and local runs.

### Out of scope
- Any change to what `upload` sends, its resume state or its report.

### Acceptance criteria
- [ ] Given a staged upload whose hash does not match the manifest, when `upload` runs, then nothing is committed and the file is reported (D-030), with both implementations.
- [ ] Given `--to azure`, when an original is committed, then the Azure implementation sets the Cold tier (a test with a recording fake of the Azure client, or the existing tier assertion).
- [ ] Given the existing `upload` tests (resume, skip unchanged work files, conflicts with another computer), when they run, then they pass on `IBlobStore`.
- [ ] Given `Sepiabox.Ingest` outside its composition root, when its source is checked, then no file uses an `Azure.Storage` namespace (the S-050 check now covers every project except `Sepiabox.Storage`, composition roots and the AppHost).

### Technical notes
- Block ids, block size (8 MB) and parallelism stay as they are; the interface takes blocks as they are read so the file is still read once.
- Decisions: D-004, D-030, D-031, D-051.

### Expected tests
- Contract tests for the staged upload on both implementations; existing `upload` unit and integration tests on `IBlobStore`.
