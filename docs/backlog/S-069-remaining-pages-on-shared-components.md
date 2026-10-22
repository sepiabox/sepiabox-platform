## S-069 — Remaining pages on the shared components
**Epic:** Visual design
**Depends on:** S-066, S-067, S-068, S-037, S-039, S-040
**Priority:** Medium
**Size:** M

### Context
Different pages do the same job with different patterns (D-119). Mockup: `docs/design/redesign/people*.html`.

### Scope
- People (named cards with avatars, a "Needs a name" section, the callout with the count), person, occasions, places, storage, sources, organize, settings and export moved onto the shared components.
- Pages that change meanwhile (sources after S-039, export after S-040) are moved as they are on main when this story starts.

### Out of scope
- Empty states and the theme switch (S-070).

### Acceptance criteria
- [ ] Given each page in `en` and `pt-BR`, light and dark, at 1024 and 390 px, then it uses the shared components and has no horizontal scroll.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119.

### Expected tests
- bUnit per page for the component classes.
