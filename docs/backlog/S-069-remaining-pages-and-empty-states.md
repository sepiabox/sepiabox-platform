## S-069 — Remaining pages, empty states and a theme switch
**Epic:** Visual design
**Depends on:** S-066, S-067, S-068
**Priority:** Medium
**Size:** M

### Context
Different pages do the same job with different patterns (D-119). Mockup: `docs/design/redesign/people*.html`.

### Scope
- People (named cards with avatars, a "Needs a name" section, the callout with the count), person, occasions, places, storage, sources, organize, settings and export moved onto the shared components.
- Illustrated empty states (SVG, no images of people), notes as toast-like cards.
- A theme switch (system / light / dark) in Settings, remembered per browser.

### Acceptance criteria
- [ ] Given each page in `en` and `pt-BR`, light and dark, at 1024 and 390 px, then it uses the shared components and has no horizontal scroll.
- [ ] Given the theme switch on "dark" with a light system setting, then the app is dark, and it stays so after a reload.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119.

### Expected tests
- bUnit per page for the component classes; the theme switch's cookie.
