## S-037 — Connect a Google Drive account
**Epic:** Cloud sources
**Depends on:** S-010
**Priority:** Medium
**Size:** M

### Context
Many of the family's photos are in Google Drive. First step: connect the account and store the token securely.

### Scope
- `/sources` page: "Connect Google Drive" (incremental OAuth, `drive.readonly` scope, offline access).
- `connected_sources` table (id, type, account_email, encrypted_refresh_token, status, last_sync_at); token protected with ASP.NET Data Protection (keys in Blob, protected by a Key Vault key in production).
- Count of photos and videos found (`files.list` with image/video `mimeType`), without importing.
- Disconnecting deletes the token. Expired token (Testing mode, 7 days) → `reconnect` status, shown as "Reconnect" ("Reconectar" in pt-BR).

### Out of scope
- Synchronization (S-038).

### Acceptance criteria
- [x] Given the OAuth callback, then the token is stored encrypted (the value in the database does not contain the token in clear text). *(`ConnectedSourcesCatalogTests`, on PostgreSQL: neither the column nor the whole row contains it; `DriveTokenProtectorTests`; the callback and its state in `SourceEndpointTests`.)*
- [x] Given a fake Drive client with 12 images and 3 videos, then the page shows 12 and 3. *(`ConnectedSourcesCatalogTests` counts them; `SourcesPageTests` shows "Photos: 12" and "Videos: 3".)*
- [x] Given `invalid_grant`, then the status becomes `reconnect`. *(Also for a token the key ring can no longer read; the page then offers Reconnect / Reconectar.)*
- [x] Given "Disconnect", then the token is deleted. *(The row is deleted and Google is asked to revoke the token; a revoke that fails still deletes.)*

### Technical notes
- `Google.Apis.Drive.v3` package. `IDriveClient` interface for tests.
- Portability (D-051): Data Protection's key storage is configured in `Program.cs` (S-032); the token code depends only on `IDataProtector`.
- Decisions: D-024, D-097.

### Expected tests
- Unit: token protection, statuses. Integration: callback and disconnect with a fake client.

### Owner acceptance
The Google Cloud steps and the check against the real Google are the owner's (`docs/operations.md`: "Connecting Google Drive" and the row of "Owner acceptance"): no test reaches Google, so `GoogleDrive` itself is untested until then.

### Status
- `done` (2026-10-22): implemented and tested with fakes (D-097); the owner steps above remain.
