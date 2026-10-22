## S-039 — Connect and sync OneDrive
**Epic:** Cloud sources
**Depends on:** S-036, S-010, S-064
**Priority:** Medium
**Size:** M

### Context
The same as Google Drive, for personal Microsoft accounts, via Microsoft Graph.

### Scope
- Connect a Microsoft account with scopes `Files.Read offline_access`; token protected as in S-037 (reuse `connected_sources`).
- Daily job: `delta` on `/me/drive/root` filtering images and videos; stored `deltaLink`; new files to `incoming` in reference mode; removed ones marked.
- `reconnect` status on `invalid_grant` (as S-037).
- Reference mode (D-023), as S-038: the asset is recorded as `originalStored: false` (S-063) unless another source stored it, and in the source list of S-064 a OneDrive source gets "Open original", downloaded from OneDrive through the server.

### Out of scope
- SharePoint, work accounts.

### Acceptance criteria
- [ ] Given a fake Graph client, when the first sync runs, then the image/video files land in `incoming` and the rest do not.
- [ ] Given the `deltaLink`, when the second sync runs, then only the changes land.
- [ ] Given `invalid_grant`, then the status becomes `reconnect`.
- [ ] Given a synced OneDrive photo whose original is not stored, when its detail page renders, then the S-064 source list shows "Open original" for the OneDrive source.

### Technical notes
- **Detail page (S-064, D-095).** Extend `SourceLinks` (`src/Sepiabox.Web/Services/SourceLinks.cs`) with an internal-link kind, an `Href` plus a text key, for "Open original" through the server; its `s_cloudTypes` set is the hook where a cloud source gets no line today. Check the cloud type names against that set when this story lands: `google-drive` and this story's OneDrive type name (`onedrive` today).
- `Microsoft.Graph` package. `IOneDriveClient` interface for tests.
- Portability (D-051): write to `incoming` through `IBlobStore` (S-050).

### Expected tests
- Integration with a fake client.
