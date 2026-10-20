## S-039 — Connect and sync OneDrive
**Epic:** Cloud sources
**Depends on:** S-036, S-010
**Priority:** Medium
**Size:** M

### Context
The same as Google Drive, for personal Microsoft accounts, via Microsoft Graph.

### Scope
- Connect a Microsoft account with scopes `Files.Read offline_access`; token protected as in S-037 (reuse `connected_sources`).
- Daily job: `delta` on `/me/drive/root` filtering images and videos; stored `deltaLink`; new files to `incoming` in reference mode; removed ones marked.
- `reconnect` status on `invalid_grant` (as S-037).

### Out of scope
- SharePoint, work accounts.

### Acceptance criteria
- [ ] Given a fake Graph client, when the first sync runs, then the image/video files land in `incoming` and the rest do not.
- [ ] Given the `deltaLink`, when the second sync runs, then only the changes land.
- [ ] Given `invalid_grant`, then the status becomes `reconnect`.

### Technical notes
- `Microsoft.Graph` package. `IOneDriveClient` interface for tests.
- Portability (D-051): write to `incoming` through `IBlobStore` (S-050).

### Expected tests
- Integration with a fake client.
