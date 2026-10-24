## S-065 — Move the code `process` and the cloud worker share into Pipeline
**Epic:** Cloud sources
**Depends on:** S-036, S-063
**Priority:** Low
**Size:** S

### Context
`CloudFileProcessor` (S-036, D-107) does for one file what `ProcessCommand` does for a folder: describe it, make the derivatives the mode asks for, add the embedding, sharpness and faces. About a hundred lines of `ProcessCommand` (`Describe`, the photo and video derivative step with the index-only sizes, the rules for files left out, the optional steps) were copied rather than moved, so that S-036 did not rework the ingest tool. Two copies drift: a rule changed in one (a preview size, a left-out reason) silently differs in the other. `ProcessParityTests` runs the same photos through both and compares the manifests; it is the guard until this story removes the copy.

### Scope
- Move the shared steps into `Sepiabox.Pipeline`, as one type both callers use: describing a file (kind, size, dates through `CaptureDate.Resolve`, location, camera, dimensions), making its derivatives for a mode (keeps originals or index-only), and adding the embedding, sharpness and faces.
- `ProcessCommand` keeps the folder walk, the work folder's state, progress and reports; `CloudFileProcessor` keeps the download, the custody of the original, the manifest write and the deletion of the incoming blob.
- No change to what either writes: the manifests, derivatives and exit codes stay as they are.

### Acceptance criteria
- [x] `ProcessCommand` and `CloudFileProcessor` contain no copy of the describe or derivative logic.
- [x] `ProcessParityTests` and the existing tests of both pass unchanged.

### Status
- `doing` (2026-10-22): `ManifestDescriber`, `DerivativeMaker` and `ManifestSteps` in `Sepiabox.Pipeline/Processing` (D-118); both callers use them, and the existing Ingest, Pipeline and Cloud tests pass unchanged.
