## S-073 — Sign-in and No access
**Epic:** Visual design
**Depends on:** S-069
**Priority:** Medium
**Size:** S

### Context
DaVinci's audit of the sign-in, no access page(s) after S-069 put the shared head, panels and form primitives on them (D-119, D-126). This story carries only what that audit still found missing. The app is desktop first (D-126).

### Already done in S-069
Nothing: S-069 did not touch these pages.

### Scope
- The sign-in page: a centred card on the shared Panel with the product mark, one primary button per provider and the privacy line.
- The No access page: an `EmptyState` with the signed-in address and the way to sign out or try another account.
- The error and not-found pages in the same card.

### Acceptance criteria
- [ ] Given the sign-in, no access page(s) in `en` and `pt-BR`, light and dark, at 1440 and 1920 px (and 390 px without horizontal scroll), then each item above is in place and uses the shared components.

### Technical notes
- DaVinci reviews every page. Decisions: D-050, D-119, D-126.

### Expected tests
- bUnit per page for the component classes and the new markup.
