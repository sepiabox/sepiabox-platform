## S-010 — Sign in with Google or Microsoft and allow only the access list
**Epic:** Web app
**Depends on:** S-009
**Priority:** High
**Size:** M

### Context
Sepiabox is private to two people. Login proves identity; the email list decides who gets in (D-006).

### Scope
- Cookie authentication + `AddGoogle` + `AddMicrosoftAccount`; configuration `Auth:Google:*`, `Auth:Microsoft:*`, `Access:Emails`.
- `OnTicketReceived`: email not on the list → redirect to `/sem-acesso` and call `HandleResponse()` (no cookie).
- `FallbackPolicy`: authenticated and on the list, for every page and API; `/entrar`, `/sem-acesso` and `/health` anonymous.
- `/entrar` page with two buttons; sign out.
- In `Development` without configured OAuth clients: `/dev-login?email=` for local testing (absent in other environments).

### Out of scope
- Apple (only with the iOS app).

### Acceptance criteria
- [ ] Given an anonymous request to `/`, when it is handled, then it is redirected to `/entrar`.
- [ ] Given a user whose email is on the list (including different casing), when they request `/`, then they get 200.
- [ ] Given an email not on the list, when `OnTicketReceived` runs, then the handler creates no session and redirects to `/sem-acesso`.
- [ ] Given an authenticated user not on the list with an old cookie, when they call the APIs, then they get 403.
- [ ] Given the `Production` environment, when I request `/dev-login`, then it returns 404.
- [ ] Given an anonymous request to `/fotos/{id}/thumb-256` or `/fotos/{id}/original`, when it is handled, then it is challenged (redirected to `/entrar`) and never redirected to storage.

### Technical notes
- `Sepiabox.Web/Auth/AccessList.cs`, `AuthSetup.cs`. Tests with `WebApplicationFactory` and a test authentication scheme.
- Real OAuth clients are a human prerequisite (`docs/operations.md`); the tests do not depend on them.
- Decisions: D-006.

### Expected tests
- Unit: `AccessList`, ticket handler. Integration (WebApplicationFactory, extending the S-009 `WebFactory`): redirects, 403, `/dev-login` per environment, and anonymous requests to the media endpoints (`/fotos/{id}/thumb-256`, `/fotos/{id}/original`) never answering with a storage URL.
