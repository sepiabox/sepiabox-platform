## S-076 — Storage and Sources
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** S

### Context
DaVinci's audit of the storage and sources page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
Both pages have the shared head and Panels; Sources has the three note variants and the danger "Disconnect" button (S-069).

### Scope
- Storage: a stat row (photos, videos, total size) above the table and a maximum width for the table so it does not stretch across 1760 px.
- Sources: the disconnect explanation next to its button, and the gap above "Connect" closed.

### Acceptance criteria
- [ ] Given the storage and sources page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
