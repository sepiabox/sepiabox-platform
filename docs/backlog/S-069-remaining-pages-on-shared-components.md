## S-069 — Remaining pages on the shared components
**Epic:** Visual design
**Depends on:** S-066, S-067, S-068, S-037, S-039, S-040
**Priority:** Medium
**Size:** M

### Context
Different pages do the same job with different patterns (D-119). Mockup: `docs/design/redesign/people*.html`.

### Scope
- The shared page parts in `Components/Parts`: `PageHead`, `SectionTitle`, `Callout`, `Notice` (success, info, warning, error), `Panel` and `EmptyState`.
- The form primitives (`.field`, `.form-row`, `.form-actions`, 40 px controls), the `.button.danger` style on every destructive submit, the desktop width (header aligned with the content, content up to 1760 px from 1600 px, 72 ch text, 720 px form columns) and the header in one row from 960 px (D-126).
- Every page moved onto the shared head and panels: people (named, "Needs a name" and the callout), person and its move, merge and delete steps, occasions, places, storage, sources, organize, settings and export.
- Each page's own remaining work is split into S-071 to S-076, which depend on this story. Pages that change meanwhile are moved as they are on main when this story starts.

### Out of scope
- The per-page redesign (S-071 to S-076); putting `EmptyState` on the pages and the theme switch (S-070).

### Acceptance criteria
- [ ] Given each page in `en` and `pt-BR`, light and dark, at 1440, 1920 and 390 px, then it uses the shared head and panels and has no horizontal scroll.
- [ ] Given a destructive submit, then it is `.button.danger`; given a refusal, then its note has `role="alert"`.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119.

### Expected tests
- bUnit per page for the component classes.
