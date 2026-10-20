## S-055 — Rename the web routes to English
**Epic:** Web app
**Depends on:** S-044
**Priority:** High
**Size:** S

### Context
Routes are English and not localized (D-050). S-009–S-011 used Portuguese routes. The app has never been published, so the old routes are renamed without redirects (D-050). Split from S-044 to keep each change small.

### Scope
- `/entrar` → `/sign-in` (and `/sign-in/{provider}`), `/sem-acesso` → `/no-access`, `/sair` → `/sign-out`, `/fotos/{id}` → `/photos/{id}`, `/fotos/{id}/{derivative}` (including `proxy-720p` and `poster`, D-049) and `/fotos/{id}/original` → `/photos/{id}/...`; query parameter `erro` → `error`.
- `AuthSetup`'s path constants, the anonymous-route list, the return-URL loop check (D-048) and the relative links in pages and components change with them.
- Update `docs/operations.md` (sign-in section, the S-032 smoke-test step and S-011's playback check) and add one line to `docs/decisions.md` saying D-039, D-048 and D-049's route names were renamed by S-055 (D-050).

### Out of scope
- Redirects from the old routes (D-050).

### Acceptance criteria
- [x] Given an anonymous request to `/`, then it is redirected to `/sign-in?ReturnUrl=%2F`.
- [x] Given a signed-in user on the list, when `/photos/{id}/thumb-256`, `/photos/{id}/proxy-720p` and `/photos/{id}/original` are requested, then they behave as the `/fotos/...` routes did (D-039, D-049).
- [x] Given someone signed in but not on the access list, when they open a page, then they are redirected to `/no-access`; media answer 403 (D-048).
- [x] Given `/entrar`, `/sem-acesso` and `/fotos/{id}`, when requested while signed in, then they answer 404; signed out, they get the sign-in redirect that any unknown address gets (D-048).
- [x] Given a return URL that is one of the new sign-in routes, then it becomes the home page (no loop).

### Technical notes
- The existing integration tests for D-048 and D-049 are the safety net: change their paths, not their assertions.
- Decisions: D-039, D-048, D-049, D-050, D-058, D-059.

### Expected tests
- Integration (`WebApplicationFactory`): the redirects and status codes of D-048 under the new paths; media endpoints under `/photos`.
