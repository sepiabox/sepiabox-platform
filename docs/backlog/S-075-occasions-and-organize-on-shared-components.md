## S-075 — Occasions and Organize
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** M

### Context
DaVinci's audit of the occasions and organize page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
Both pages have the shared head, Organize's views are pills and each burst is on a Panel (S-069).

### Scope
- Occasions: the old filter bar replaced by one dropdown chip (as the timeline's, S-067) for the type, and the occasion cards on the shared card style.
- Organize: a sticky action bar with the count of selected photos and the "Set aside" button, the grid using the width at 1440 px and up, and the subtitle held to 72 ch.

### Acceptance criteria
- [ ] Given the occasions and organize page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
